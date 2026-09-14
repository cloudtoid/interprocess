# Interprocess website

A static landing page and developer documentation for all six language APIs. No runtime framework or external dependencies.

```sh
python3 src/website/build.py
python3 -m http.server --directory src/website/dist
```

Cloudflare Pages publishes this site directly from `cloudtoid/interprocess`. Changes under `src/website/**` on `main` trigger production deployments; other branches receive previews linked from GitHub. Project: `cloudtoid`; build command: `python3 src/website/build.py`; output directory: `src/website/dist`. The Website GitHub Actions workflow also validates the build and JavaScript syntax. Failed builds do not replace the last successful deployment.

Keep package availability and installation commands current when releasing packages. Benchmarks change only after new measurements.

The header and footer use the official blue wordmarks from [cloudtoid/assets](https://github.com/cloudtoid/assets/tree/master/logos), served locally. The black and white variants follow the selected color theme.

## Developer documentation

`docs/pages.json` defines page titles, descriptions, navigation, and URLs. Edit the corresponding HTML fragments under `docs/`; `docs/template.html` supplies the shared layout. `build.py` renders the pages into `dist/docs/`, creates the sitemap and robots.txt, and adds canonical, social, and structured metadata. API content is authored against the public implementations; it is not generated from source comments. Update the reference alongside API changes, including waiting, error, ownership, and truncation behavior. Link to generated ecosystem references where available.

Run `python3 src/website/validate.py` after building to check local links, anchors, metadata, and sitemap coverage. CI runs this check. All reference text and navigation work without JavaScript; `docs.js` enhances code blocks with highlighting and copy buttons. The homepage's language guide links point to these pages.

The social preview uses `assets/social-card.png`; its editable SVG source is alongside it. SEO metadata uses `https://cloudtoid.com` as the canonical origin. Publishing makes the sitemap available at `/sitemap.xml`; search-engine indexing happens independently of deployment.
