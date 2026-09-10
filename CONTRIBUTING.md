# Contributing

## Local setup

Install .NET SDK 10, Node.js 22+, and npm. Follow the development commands in [README.md](README.md), using a disposable `.data` directory. Deployment recipes live in [packaging/](packaging/README.md); implementation boundaries and UI guidelines live in [docs/architecture.md](docs/architecture.md) and [docs/design.md](docs/design.md).

## Before opening a pull request

```bash
dotnet tool restore
dotnet restore SpeedtestDashboard.sln --locked-mode -p:AuditPipeline=true
dotnet format SpeedtestDashboard.sln --verify-no-changes --no-restore
dotnet build SpeedtestDashboard.sln --configuration Release --no-restore -p:AuditPipeline=true
dotnet test SpeedtestDashboard.sln --configuration Release --no-build
dotnet ef migrations has-pending-model-changes \
  --project src/SpeedtestDashboard.Infrastructure \
  --startup-project src/SpeedtestDashboard.Api
npm ci --prefix src/SpeedtestDashboard.Web
npm run verify:dependencies --prefix src/SpeedtestDashboard.Web
npm run check --prefix src/SpeedtestDashboard.Web
npm test --prefix src/SpeedtestDashboard.Web
npm run build --prefix src/SpeedtestDashboard.Web
```

Tests must not run a public bandwidth test. Use fixtures and fake providers.

## Provider safety

- Never pass request data through a shell.
- Do not accept executable paths, command strings, URLs, environment variables, or arbitrary CLI flags from HTTP.
- Keep arguments in `ProcessStartInfo.ArgumentList`.
- Bound execution time and output size, preserve cancellation, and sanitize errors.
- Keep provider-specific parsing and unit conversion in its adapter.
- Do not add result sharing, telemetry, certificate bypasses, or privileged networking by default.
- Review redistribution terms before adding third-party binaries to public artifacts.

## Dependencies

Commit lockfiles. Non-Microsoft packages use a 14-day release-age cooldown enforced by `scripts/check-package-age.mjs`; known vulnerabilities still block an update regardless of age. Microsoft platform packages may update without the cooldown.

## Pull requests

Keep changes focused. Update tests, user-facing copy, configuration documentation, and third-party notices when behavior changes. Describe manual checks and anything that could not be verified, especially architecture, container, or Proxmox runtime claims.

## Repository hygiene and packaging

Keep local evaluations and scratch notes in ignored `.local/` or outside the
checkout. `artifacts/` is ignored build output. Never commit runtime databases,
Data Protection keys, local environment files, credentials, or built appliances.
Use sanitized examples and fixtures when adding documentation or tests.

Before staging, review `git status --short` and
`git ls-files -ci --exclude-standard`; ignore rules do not untrack existing files
or erase history. See [release maintenance](docs/releasing.md) before publishing
an existing private repository.

When changing packaging, check both [container](packaging/containers/README.md)
and [native LXC](packaging/proxmox/README.md) paths. Keep provider pins in
`packaging/providers/`, verify checksum failures stop the build, and preserve
explicit Ookla acceptance. Public release builds must exclude the Ookla CLI.
