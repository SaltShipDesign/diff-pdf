# diff-pdf

Running diff-pdf through Docker is the only supported way to use this tool.

[![Build](https://github.com/vslavik/diff-pdf/actions/workflows/build.yml/badge.svg)](https://github.com/vslavik/diff-pdf/actions/workflows/build.yml)

## Creating a release

Create and push a `v*` tag to build the release:

```sh
git tag -a v1.2.3 -m "Release version 1.2.3"
git push origin v1.2.3
```

The tag workflow builds the Linux binary, verifies the Docker runtime, builds
the job-dispatcher Docker image, verifies that the dispatcher image contains
the Linux `diff-pdf` binary built by the same workflow run, and publishes the
saved job-dispatcher image tarball to the GitHub release.

## Job dispatcher image

The job-dispatcher image is the primary runtime. It authenticates with the
configured API, polls for jobs, downloads source PDFs over `scp`, runs diff-pdf
inside Docker, and uploads the generated diff PDF.

Build from the repository root:

```sh
docker build -f job-dispatcher/Dockerfile -t diff-pdf-job-dispatcher .
```

The image defaults to `linux/amd64` because the bundled `diff-pdf-b` binary and
pinned Poppler packages are x86-64.

Run the dispatcher with API credentials and an SSH config containing the
configured host alias, `pepper` by default:

```sh
docker run --rm \
  -e PDFDIFF_API_LOGIN=pdfdiff \
  -e PDFDIFF_API_PASSWORD=... \
  -e API_PASSWORD=... \
  -e WORKER_ID=292 \
  -v "$HOME/.ssh:/root/.ssh:ro" \
  diff-pdf-job-dispatcher
```

The dispatcher downloads source PDFs with `scp`, so the host OS must have an
SSH key and SSH config that can reach the configured host alias before starting
the container. The examples mount the host SSH directory read-only into the
container as `/root/.ssh`. With the default `SCP_HOST_ALIAS=pepper`, the host
SSH config should contain a matching entry:

```sshconfig
Host pepper
  HostName pepper.saltship.com
  User pdfdiff
  IdentityFile ~/.ssh/id_ed25519
```

Use the actual Pepper SSH user, host name, and key path for the deployment. The
private key should stay on the host and should not be copied into the Docker
image.

Run one polling cycle and keep local job artifacts for inspection:

```sh
docker run --rm \
  -e PDFDIFF_API_LOGIN=pdfdiff \
  -e PDFDIFF_API_PASSWORD=... \
  -e API_PASSWORD=... \
  -e RUN_ONCE=true \
  -e DELETE_ARTIFACTS=false \
  -v "$HOME/.ssh:/root/.ssh:ro" \
  diff-pdf-job-dispatcher
```

Override the API endpoint and SSH host alias:

```sh
docker run --rm \
  -e API_ENDPOINT=https://example.com/api/ \
  -e SCP_HOST_ALIAS=pdf-server \
  -e PDFDIFF_API_LOGIN=pdfdiff \
  -e PDFDIFF_API_PASSWORD=... \
  -e API_PASSWORD=... \
  -v "$HOME/.ssh:/root/.ssh:ro" \
  diff-pdf-job-dispatcher
```

Dispatcher environment variables:

| Variable | Default |
| --- | --- |
| `API_ENDPOINT` | `https://pepper.saltship.com/api/` |
| `PEPPER_UPLOAD_URL` | `${API_ENDPOINT}pdfdiff/uploadDiff` |
| `PDFDIFF_API_LOGIN` | required |
| `PDFDIFF_API_PASSWORD` | required |
| `API_PASSWORD` | required |
| `WORKER_ID` | `292` |
| `WORK_DIR` | `/work` |
| `SCP_HOST_ALIAS` | `pepper` |
| `DIFF_PDF_PATH` | `/usr/local/bin/diff-pdf` |
| `DIFF_PDF_DISPLAY` | `99` |
| `POLL_INTERVAL_SECONDS` | `5` |
| `COMMAND_TIMEOUT_SECONDS` | `600` |
| `HTTP_TIMEOUT_SECONDS` | `120` |
| `RUN_ONCE` | `false` |
| `DELETE_ARTIFACTS` | `true` |

## Manual PDF diff image

Use this image when you have two local PDF files and want a generated visual
diff PDF.

Build the image:

```sh
docker build -f docker-root/Dockerfile -t diff-pdf-image .
```

Run it by mounting a host directory containing the input PDFs to `/data`.
The container accepts two input PDF filenames and an optional output filename.
If the output filename is omitted, it writes `diff.pdf`.

```sh
docker run --rm \
  -v /absolute/path/to/pdfs:/data \
  diff-pdf-image file1.pdf file2.pdf output-diff.pdf
```

Example using the repository test PDFs:

```sh
docker build -f docker-root/Dockerfile -t diff-pdf-image .
docker run --rm \
  -v "$PWD/test:/data" \
  diff-pdf-image 0217-101-001-C.pdf 0217-101-001-I.pdf output-diff.pdf
```

After the container exits, the output PDF is available in the mounted host
directory.
