"""Check rendered page metadata, structured data, local links, and fragment targets."""
from html.parser import HTMLParser
from hashlib import sha256
import json
from pathlib import Path
from urllib.parse import urljoin, urlparse, unquote
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent / 'dist'
base = 'https://cloudtoid.com'


class Page(HTMLParser):
    def __init__(self, path):
        super().__init__(convert_charrefs=True)
        self.path, self.ids, self.links, self.meta = path, set(), [], {}
        self.canonicals, self.titles, self.h1s, self.json_data = [], [], [], []
        self.capture, self.text = None, ''
        self.feed(path.read_text())

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if 'id' in attrs:
            assert attrs['id'] not in self.ids, f'{self.path}: duplicate id {attrs["id"]}'
            self.ids.add(attrs['id'])
        if tag == 'a' and 'href' in attrs:
            self.links.append(attrs['href'])
        if tag in ('img', 'script') and 'src' in attrs:
            self.links.append(attrs['src'])
        if tag == 'link':
            if attrs.get('rel') == 'canonical':
                self.canonicals.append(attrs['href'])
            elif attrs.get('rel') == 'stylesheet':
                self.links.append(attrs['href'])
        if tag == 'meta':
            key = attrs.get('name', attrs.get('property'))
            if key:
                self.meta[key] = attrs.get('content')
        if tag in ('title', 'h1') or (tag == 'script' and attrs.get('type') == 'application/ld+json'):
            self.capture, self.text = tag, ''

    def handle_data(self, data):
        if self.capture:
            self.text += data

    def handle_endtag(self, tag):
        if tag == self.capture:
            if tag == 'title':
                self.titles.append(self.text)
            elif tag == 'h1':
                self.h1s.append(self.text)
            else:
                self.json_data.append(json.loads(self.text))
            self.capture = None


pages = {p: Page(p) for p in root.rglob('*.html')}
expected = ['index.html', 'docs/index.html'] + [f'docs/{slug}/index.html' for slug in ('concepts', 'rust', 'node', 'go', 'c', 'python', 'dotnet')]
assert all(root / path in pages for path in expected), 'Missing documentation pages'
titles, descriptions, canonicals = set(), set(), set()
for path, page in pages.items():
    relative = path.relative_to(root).as_posix()
    url = base + '/' + relative.removesuffix('index.html')
    assert page.canonicals == [url], f'{path}: incorrect canonical'
    assert len(page.titles) == len(page.h1s) == 1, f'{path}: title/h1 missing or duplicated'
    assert page.titles[0] not in titles, f'{path}: duplicate title'
    titles.add(page.titles[0])
    description = page.meta.get('description')
    assert description and description not in descriptions, f'{path}: missing/duplicate description'
    descriptions.add(description)
    assert page.meta.get('og:url') == url and page.meta.get('og:title'), f'{path}: missing social metadata'
    assert page.json_data, f'{path}: missing structured data'
    assert page.meta.get('robots') != 'noindex', f'{path}: accidentally excluded from indexing'
    canonicals.add(url)
    for link in page.links + [page.meta['og:image']]:
        target = urlparse(urljoin(url, link))
        if target.netloc != 'cloudtoid.com' or target.scheme not in ('http', 'https'):
            continue
        local = root / unquote(target.path).lstrip('/')
        if local.is_dir():
            local /= 'index.html'
        assert local.is_file(), f'{path}: broken local link {link}'
        if local.suffix in ('.css', '.js'):
            digest = sha256(local.read_bytes()).hexdigest()[:12]
            assert f'.{digest}{local.suffix}' in local.name, f'{path}: unversioned asset {link}'
        if target.fragment and local in pages:
            assert unquote(target.fragment) in pages[local].ids, f'{path}: missing anchor {link}'

sitemap = ET.parse(root / 'sitemap.xml')
locations = [item.text for item in sitemap.findall('.//{http://www.sitemaps.org/schemas/sitemap/0.9}loc')]
assert len(locations) == len(set(locations)) and set(locations) == canonicals, 'Sitemap does not match pages'
assert 'Sitemap: https://cloudtoid.com/sitemap.xml' in (root / 'robots.txt').read_text()
print(f'Validated {len(pages)} pages: metadata, sitemap, structured JSON, local assets, links, and anchors')
