# Interprocess website

A static landing page with language examples and links to the library and protocol. No runtime framework or external dependencies.

```sh
python3 src/website/build.py
python3 -m http.server --directory src/website/dist
```

The Website workflow validates pull requests and publishes to GitHub Pages after merging into main. Configure the repository's Pages source as GitHub Actions. The public site at cloudtoid.com uses Sites hosting with the same source; `.openai/hosting.json` identifies it. Keep registry availability and the preview/release notice accurate when publishing packages.
