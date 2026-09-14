# Building and releasing the language packages

The .NET library stays independent under `src/dotnet`, with its own NuGet publishing job in the shared release workflow. Native packages share one Rust workspace version. Their public APIs wrap the same protocol v3 core; changing a package version does not automatically change the protocol.

## Build files

| Component | Build entry point |
|---|---|
| .NET | `src/dotnet/Interprocess.sln` |
| Rust | `src/rust/Cargo.toml` |
| C SDK | `src/c/CMakeLists.txt` and `src/c/Cargo.toml` |
| Python | `src/python/pyproject.toml` |
| Node.js | `src/node/package.json` and `build.js` |
| Go | `src/go/go.mod` (cgo; installed C SDK required) |
| Website | `src/website/build.py` |

The Native core workflow checks Rust formatting, linting, and the C ABI on Linux, macOS, and Windows, plus core tests on the minimum Rust version. Language interoperability runs the stable core crash-recovery tests, builds actual bindings, tests every ordered pair, and runs six publishers with six competing subscribers across participant crashes on Linux x64/ARM64, macOS, and Windows. Existing .NET job names remain unchanged for branch protection. Release builds are used throughout; no Debug-only behavior is required.

## Initial registry setup

All packages must use Cloudtoid branding and organization ownership wherever the registry supports it. Personal logins authorize publishing; they do not replace organization ownership. Before publishing to each registry, connect its owners and names:

- crates.io: `cloudtoid-interprocess` and `cloudtoid-interprocess-ffi`. crates.io uses global crate names and GitHub team owners. The `cloudtoid/interprocess` GitHub team exists, with `prezaei` as its maintainer. After initial creation, add `github:cloudtoid:interprocess` as an owner of both crates and verify it with `cargo owner --list`. This needs ownership-management authorization, which the initial publish-only token does not grant. Keep the personal account for ownership administration. Configure `CARGO_REGISTRY_TOKEN` in the `crates-io` GitHub environment with permission to publish these crates; after the initial publish, configure both crates to trust `cloudtoid/interprocess`, workflow `publish.yml`, environment `crates-io`. Remove the initial secret once that configuration is verified; the workflow then obtains short-lived tokens using `rust-lang/crates-io-auth-action`.
- PyPI: wait for the `cloudtoid` organization request to be approved. Create `cloudtoid-interprocess` through that organization's Projects page, then configure its trusted publisher: GitHub owner `cloudtoid`, repository `interprocess`, workflow `publish.yml`, environment `pypi`. Do not use a personal-account pending publisher to create the project. Confirm organization ownership before publication; the `Cloudtoid` author field alone is not ownership. No long-lived PyPI token is required.
- npm: use the `@cloudtoid` scope. Configure each of the six packages to trust GitHub repository `cloudtoid/interprocess`, workflow `publish.yml`, environment `npm`, with permission to publish. The workflow uses OIDC without `NPM_TOKEN`; provenance is automatic. Publish the platform packages and main package under the same scope.
- Go: versions come from the repository tag `src/go/v3.0.0`, matching module path `github.com/cloudtoid/interprocess/src/go/v3`. There is no separate account to create.
- C SDK: GitHub release assets in `cloudtoid/interprocess` contain the native library, header, pkg-config file, and license.

Keep credentials in registry/GitHub settings, never in source files or chat messages. Registry authorization is separate from GitHub push access.

## Release

1. Update `Cargo.toml`, `src/python/pyproject.toml`, Python `__version__`, and `src/node/package.json` together, including Node optional dependency versions and the CMake SDK version. Update the core dependency versions in wrapper manifests when the core version changes.
2. Merge reviewed code into main. Stable native publication is forbidden from feature branches; the workflow checks this before building.
3. Run **Release packages** (`publish.yml`) from main. This is the single manual release entry point for all registries; merges run validation but do not publish automatically. It rebuilds/tests all native packages and all language pairs, cross-builds Intel Mac artifacts on an Apple Silicon runner, assembles npm packages and Python wheels/source distribution, then publishes registries. NuGet, crates.io, npm, PyPI, and the C SDK/Go release run independently after their required validation/build jobs succeed; one registry failure does not block another. Python publication is independent: leave **Publish Python packages to PyPI** unchecked when PyPI setup is not ready. Python wheels and the source distribution are still built and retained in the release artifacts.
4. Install from each public registry in fresh environments, rerun representative cross-language delivery, and update the website/README preview notice after availability is verified.

NuGet retains its existing `3.0.<workflow run number>` version numbering. The workflow stays at `publish.yml` to preserve its existing NuGet trusted-publisher identity and run counter.

Use the **Registry to publish** selector to retry just one registry after a workflow fix, without attempting to recreate existing C SDK/Go release tags. If no workflow fix is needed, rerun the failed jobs at the same main commit. Already published versions are skipped; existing release tags must point to that commit. Retire the initial tokens after verifying trusted publishing; future releases use short-lived workflow credentials.

Binary targets: Linux x64 and ARM64 (glibc 2.34+), macOS ARM64 and x64, Windows x64. The x64 Mac release is cross-built; it does not add a permanent Intel Mac CI runner. Rust/C source builds support the core's platform/architecture restrictions. Python includes a source distribution. Go links the installed C SDK; its module does not silently download binaries.

The Website workflow validates the static build. Publish the public website at cloudtoid.com through its existing Sites hosting project. Advertise actual package availability and measured performance; do not label in-process microbenchmarks as application-to-application latency.
