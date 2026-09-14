# Interprocess website

A static landing page with language examples and links to the library and protocol. No runtime framework or external dependencies.

```sh
python3 src/website/build.py
python3 -m http.server --directory src/website/dist
```

Cloudflare Pages publishes this site directly from `cloudtoid/interprocess`. Changes under `src/website/**` on `main` trigger production deployments; other branches receive previews linked from GitHub. Project: `cloudtoid`; build command: `python3 src/website/build.py`; output directory: `src/website/dist`. The Website GitHub Actions workflow also validates the build and JavaScript syntax. Failed builds do not replace the last successful deployment.

Keep package availability and installation commands current when releasing packages. Benchmarks change only after new measurements.

The header and footer use the official blue wordmarks from [cloudtoid/assets](https://github.com/cloudtoid/assets/tree/master/logos), served locally. The black and white variants follow the selected color theme.
