"""Build the static marketing site and developer reference, without dependencies."""
from html import escape
from hashlib import sha256
import json
from pathlib import Path
import re
import shutil
from string import Template

root = Path(__file__).resolve().parent
output = root / 'dist'
output.mkdir(exist_ok=True)
base = 'https://cloudtoid.com'
pages = json.loads((root / 'docs/pages.json').read_text())
home = (root / 'index.html').read_text()
favicon = re.search(r'<link rel="icon"[^>]+>', home).group()


def metadata(title, description, path, kind='website'):
    url = base + path
    tags = [f'<link rel="canonical" href="{url}">']
    for key, value in {
        'og:type': kind, 'og:site_name': 'Cloudtoid Interprocess',
        'og:title': title, 'og:description': description, 'og:url': url,
        'og:image': base + '/assets/social-card.png',
        'og:image:width': '1200', 'og:image:height': '630',
        'og:image:alt': 'Cloudtoid Interprocess: fast shared-memory queues across six languages',
    }.items():
        tags.append(f'<meta property="{key}" content="{escape(value, quote=True)}">')
    tags.append('<meta name="twitter:card" content="summary_large_image">')
    return '\n'.join(tags)


home_title = re.search(r'<title>(.*?)</title>', home).group(1)
home_description = re.search(r'<meta name="description" content="([^"]+)"', home).group(1)
home = re.sub(r'<link rel="canonical"[^>]+>', metadata(home_title, home_description, '/'), home)
software = {
    '@context': 'https://schema.org', '@type': 'SoftwareSourceCode',
    'name': 'Cloudtoid Interprocess', 'url': base + '/',
    'description': home_description,
    'codeRepository': 'https://github.com/cloudtoid/interprocess',
    'programmingLanguage': ['Rust', 'C', 'Python', 'JavaScript', 'Go', 'C#'],
    'runtimePlatform': ['Linux', 'macOS', 'Windows'],
    'license': 'https://github.com/cloudtoid/interprocess/blob/main/LICENSE',
}
home = home.replace('</head>', '<script type="application/ld+json">' + json.dumps(software) + '</script>\n</head>')
(output / 'index.html').write_text(home)
for name in ('style.css', 'site.js', 'theme.js', 'docs.css', 'docs.js', 'protocol.js'):
    shutil.copyfile(root / name, output / name)
for name in ('assets', 'benchmarks', 'vendor'):
    shutil.copytree(root / name, output / name, dirs_exist_ok=True)


def page_path(page):
    return '/docs/' + (page['slug'] + '/' if page['slug'] else '')


template = Template((root / 'docs/template.html').read_text())
for page in pages:
    path = page_path(page)
    content = (root / 'docs' / ((page['slug'] or 'overview') + '.html')).read_text()
    navigation = ''.join(
        '<a href="{}"{}>{}</a>'.format(page_path(item), ' aria-current="page"' if item == page else '', escape(item['label']))
        for item in pages
    )
    headings = re.findall(r'<h2 id="([^"]+)">(.*?)</h2>', content)
    content = re.sub(r'<h([23]) id="([^"]+)">(.*?)</h\1>',
        lambda match: f'<h{match[1]} id="{match[2]}"><a class="heading-link" href="#{match[2]}">{match[3]}</a></h{match[1]}>', content)
    toc = ''.join(f'<a href="#{key}">{title}</a>' for key, title in headings)
    breadcrumbs = [{'@type': 'ListItem', 'position': 1, 'name': 'Documentation', 'item': base + '/docs/'}]
    if page['slug']:
        breadcrumbs.append({'@type': 'ListItem', 'position': 2, 'name': page['title'], 'item': base + path})
    data = [
        {'@context': 'https://schema.org', '@type': 'TechArticle', 'headline': page['title'],
         'description': page['description'], 'url': base + path, 'inLanguage': 'en',
         'author': {'@type': 'Organization', 'name': 'Cloudtoid', 'url': base + '/'}},
        {'@context': 'https://schema.org', '@type': 'BreadcrumbList', 'itemListElement': breadcrumbs},
    ]
    rendered = template.substitute(
        title=escape(page['title']), description=escape(page['description'], quote=True),
        metadata=metadata(page['title'] + ' | Cloudtoid Interprocess', page['description'], path, 'article')
        + '\n<script type="application/ld+json">' + json.dumps(data) + '</script>',
        favicon=favicon, navigation=navigation, toc=toc, content=content,
        breadcrumb=('<span>/</span><span>' + escape(page['label']) + '</span>') if page['slug'] else '',
    )
    target = output / path.strip('/') / 'index.html'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(rendered)

urls = [base + '/'] + [base + page_path(page) for page in pages]
(output / 'sitemap.xml').write_text('<?xml version="1.0" encoding="UTF-8"?>\n'
    + '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n'
    + '\n'.join(f'<url><loc>{url}</loc></url>' for url in urls) + '\n</urlset>\n')
(output / 'robots.txt').write_text('User-agent: *\nAllow: /\n\nSitemap: ' + base + '/sitemap.xml\n')
# Content-addressed assets prevent browsers reusing scripts/styles from an older deploy.
assets = {}
for page in output.rglob('*.html'):
    def fingerprint(match):
        url = match[2]
        if "://" in url or url.startswith("//"):
            return match[0]
        source = output / url.lstrip('/')
        if url not in assets:
            digest = sha256(source.read_bytes()).hexdigest()[:12]
            target = source.with_name(f'{source.stem}.{digest}{source.suffix}')
            shutil.copyfile(source, target)
            assets[url] = '/' + target.relative_to(output).as_posix()
        return f'{match[1]}="{assets[url]}"'
    page.write_text(re.sub(r'(src|href)="([^"?]+\.(?:css|js))"', fingerprint, page.read_text()))

print(f'Built {output}: homepage and {len(pages)} documentation pages')
