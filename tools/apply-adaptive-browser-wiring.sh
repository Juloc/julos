#!/usr/bin/env bash
set -euo pipefail

python3 <<'PY'
from pathlib import Path


def read(path: str) -> str:
    return Path(path).read_text(encoding='utf-8-sig')


def write(path: str, text: str) -> None:
    Path(path).write_text(text, encoding='utf-8')


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise SystemExit(f"missing patch anchor: {label}")
    if text.count(old) != 1:
        raise SystemExit(f"patch anchor is not unique: {label}")
    return text.replace(old, new, 1)

validate_path = 'tools/validate.mjs'
validate = read(validate_path)
validate = replace_once(
    validate,
    "const browserFrontendDirectory = join(repositoryRoot, 'packages', 'JulOS.Browser', 'frontend');\n",
    "const browserFrontendDirectory = join(repositoryRoot, 'packages', 'JulOS.Browser', 'frontend');\n"
    "const adaptiveBrowserFrontendDirectory = join(repositoryRoot, 'packages', 'JulOS.AdaptiveBrowser', 'frontend');\n",
    'adaptive frontend directory',
)
validate_stage = """  {
    name: 'browser-frontend-test',
    title: 'Run Browser package frontend tests',
    run: () => run('npm', ['test'], browserFrontendDirectory),
  },
"""
validate = replace_once(
    validate,
    validate_stage,
    validate_stage + """  {
    name: 'adaptive-browser-frontend-test',
    title: 'Run Adaptive Browser package frontend tests',
    run: () => run('npm', ['test'], adaptiveBrowserFrontendDirectory),
  },
""",
    'adaptive frontend validation stage',
)
write(validate_path, validate)

release_path = '.github/workflows/release.yml'
release = read(release_path)
release = replace_once(
    release,
    "          - browser-runtime\n          - session-runtimes\n          - remote-transport\n",
    "          - browser-runtime\n          - adaptive-browser-runtimes\n          - session-runtimes\n          - remote-transport\n",
    'release target option',
)
release = replace_once(
    release,
    "  JULOS_BROWSER_RUNTIME_IMAGE: ghcr.io/juloc/julos-browser-runtime@sha256:14ecfacb71a0e461351e2e87c19b80413d0f704ca2270f060c64404a6903743d\n",
    "  JULOS_BROWSER_RUNTIME_IMAGE: ghcr.io/juloc/julos-browser-runtime@sha256:14ecfacb71a0e461351e2e87c19b80413d0f704ca2270f060c64404a6903743d\n"
    "  JULOS_ADAPTIVE_BROWSER_RUNTIME_IMAGE: \"\"\n",
    'adaptive runtime digest environment',
)
release = replace_once(
    release,
    "        run: sh tools/validate.sh --stage remote-frontend-install --stage remote-frontend-build --stage remote-frontend-test --stage browser-frontend-test\n",
    "        run: sh tools/validate.sh --stage remote-frontend-install --stage remote-frontend-build --stage remote-frontend-test --stage browser-frontend-test --stage adaptive-browser-frontend-test\n",
    'release frontend validation',
)
release = replace_once(
    release,
    '          bash tools/stage-official-package-catalog.sh deploy/official-packages "$JULOS_BROWSER_RUNTIME_IMAGE"\n',
    '          bash tools/stage-official-package-catalog.sh deploy/official-packages "$JULOS_BROWSER_RUNTIME_IMAGE" "$JULOS_ADAPTIVE_BROWSER_RUNTIME_IMAGE"\n',
    'official package staging',
)
release = replace_once(
    release,
    '            echo "- Browser runtime pinned in package catalog: $JULOS_BROWSER_RUNTIME_IMAGE"\n',
    '            echo "- Browser runtime pinned in package catalog: $JULOS_BROWSER_RUNTIME_IMAGE"\n'
    '            echo "- Adaptive Browser runtime pinned in package catalog: $JULOS_ADAPTIVE_BROWSER_RUNTIME_IMAGE"\n',
    'release summary adaptive runtime',
)

marker = "\n  remote-transport:\n"
adaptive_job = """
  adaptive-browser-runtimes:
    name: Publish Adaptive Browser runtimes
    if: inputs.target == 'adaptive-browser-runtimes'
    needs: [validate-dotnet, validate-desktop, validate-frontends, validate-meta, validate-container]
    runs-on: ubuntu-latest
    timeout-minutes: 70
    permissions:
      contents: read
      packages: write
      id-token: write
      attestations: write
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup QEMU
        uses: docker/setup-qemu-action@v4

      - name: Setup Docker Buildx
        uses: docker/setup-buildx-action@v4

      - name: Login to GHCR
        uses: docker/login-action@v4
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Refuse existing images
        shell: bash
        run: |
          set -euo pipefail
          version="${{ needs.validate-dotnet.outputs.version }}"
          for image in \
            "ghcr.io/juloc/julos-adaptive-browser-runtime:$version" \
            "ghcr.io/juloc/julos-adaptive-browser-provider:$version"; do
            if docker manifest inspect "$image" >/dev/null 2>&1; then
              echo "Adaptive Browser runtime image version already exists: $image" >&2
              exit 1
            fi
          done

      - name: Publish Adaptive Browser Runtime
        id: runtime
        uses: docker/build-push-action@v7
        with:
          context: .
          file: packages/JulOS.AdaptiveBrowser/runtime/Dockerfile
          platforms: linux/amd64
          push: true
          cache-from: type=gha,scope=julos-adaptive-browser-runtime
          cache-to: type=gha,mode=max,scope=julos-adaptive-browser-runtime
          tags: ghcr.io/juloc/julos-adaptive-browser-runtime:${{ needs.validate-dotnet.outputs.version }}
          build-args: |
            JULOS_VERSION=${{ needs.validate-dotnet.outputs.version }}
          provenance: false
          sbom: true

      - name: Attest Adaptive Browser Runtime
        uses: actions/attest@v4
        with:
          subject-name: ghcr.io/juloc/julos-adaptive-browser-runtime
          subject-digest: ${{ steps.runtime.outputs.digest }}
          push-to-registry: true

      - name: Publish Adaptive Browser Provider
        id: provider
        uses: docker/build-push-action@v7
        with:
          context: .
          file: packages/JulOS.AdaptiveBrowser/provider/Dockerfile
          platforms: linux/amd64
          push: true
          cache-from: type=gha,scope=julos-adaptive-browser-provider
          cache-to: type=gha,mode=max,scope=julos-adaptive-browser-provider
          tags: ghcr.io/juloc/julos-adaptive-browser-provider:${{ needs.validate-dotnet.outputs.version }}
          build-args: |
            JULOS_VERSION=${{ needs.validate-dotnet.outputs.version }}
          provenance: false
          sbom: true

      - name: Attest Adaptive Browser Provider
        uses: actions/attest@v4
        with:
          subject-name: ghcr.io/juloc/julos-adaptive-browser-provider
          subject-digest: ${{ steps.provider.outputs.digest }}
          push-to-registry: true

      - name: Runtime summary
        shell: bash
        run: |
          {
            echo "- Adaptive Browser Runtime: ghcr.io/juloc/julos-adaptive-browser-runtime@${{ steps.runtime.outputs.digest }}"
            echo "- Adaptive Browser Provider: ghcr.io/juloc/julos-adaptive-browser-provider@${{ steps.provider.outputs.digest }}"
          } >> "$GITHUB_STEP_SUMMARY"
"""
release = replace_once(release, marker, "\n" + adaptive_job + marker, 'adaptive browser release job')
write(release_path, release)
PY

node tools/normalize-encoding.mjs
