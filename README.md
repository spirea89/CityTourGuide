# Environment and Secret Management

This project uses environment variables for API credentials to avoid committing
secrets to version control.

## Local development

1. Copy the example file and fill in your API keys:
   ```bash
   cp .env.example .env
   ```
2. Install the appropriate dotenv loader for the language runtime you need.

### Node.js example

Install [`dotenv`](https://www.npmjs.com/package/dotenv) and load environment
variables as early as possible in your application:

```javascript
// npm install dotenv
const dotenv = require("dotenv");

dotenv.config();

const openAiKey = process.env.OPENAI_API_KEY;
const googleKey = process.env.GOOGLE_API_KEY;

if (!openAiKey || !googleKey) {
  throw new Error("Missing required API keys. Check your .env file.");
}

console.log("OpenAI key loaded:", `${openAiKey.slice(0, 7)}…`);
console.log("Google key loaded:", `${googleKey.slice(0, 7)}…`);
```

### Python example

Install [`python-dotenv`](https://pypi.org/project/python-dotenv/) and load the
variables before accessing them:

```python
# pip install python-dotenv
from dotenv import load_dotenv
import os

load_dotenv()

openai_key = os.getenv("OPENAI_API_KEY")
google_key = os.getenv("GOOGLE_API_KEY")

if not openai_key or not google_key:
    raise RuntimeError("Missing required API keys. Check your .env file.")

print(f"OpenAI key loaded: {openai_key[:7]}…")
print(f"Google key loaded: {google_key[:7]}…")
```

## GitHub Actions

The [`ci-secrets-example` workflow](.github/workflows/ci-secrets-example.yml)
shows how to inject API keys stored as repository secrets into a CI job:

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    env:
      OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
      GOOGLE_API_KEY: ${{ secrets.GOOGLE_API_KEY }}
    steps:
      - uses: actions/checkout@v4
      - name: Show masked keys
        run: |
          echo "OpenAI key length: ${#OPENAI_API_KEY}"
          echo "Google key length: ${#GOOGLE_API_KEY}"
```

Define the `OPENAI_API_KEY` and `GOOGLE_API_KEY` secrets in the repository
settings so the workflow can access them securely.

## Pre-commit secret scanning

This repository includes a [`pre-commit`](https://pre-commit.com/) hook powered
by [gitleaks](https://github.com/gitleaks/gitleaks) to prevent committing
secrets:

```bash
pip install pre-commit
pre-commit install
```

Running `pre-commit run --all-files` will scan the repository for potential
secrets before they reach version control.

## Troubleshooting low disk space during builds

The .NET MAUI workloads produce a large amount of intermediate output in the
`bin/` and `obj/` folders for each target platform. When these folders are not
periodically cleaned they can easily consume several gigabytes of storage and
cause build failures like `System.IO.IOException: There is not enough space on
the disk`.

To reclaim space safely:

1. Close any running IDE instances (Visual Studio, VS Code, Rider, etc.) so
   they release file handles on the build folders.
2. Run the cleanup script that ships with this repository:

   ```powershell
   pwsh ./scripts/clear-build-storage.ps1
   ```

   The script deletes every `bin/` and `obj/` directory under the solution
   folder and reports the amount of reclaimed space. You can pass
   `-IncludeNuGetCache` and `-IncludeWorkloads` switches when additional space is
   needed to clear the global NuGet cache and unused .NET workloads
   respectively.

   On macOS or Linux you can instead run the equivalent Bash script:

   ```bash
   ./scripts/clear-build-storage.sh
   ```

3. Retry the build. With the temporary outputs removed the project typically
   rebuilds successfully.

If disk space is still tight, consider moving the repository to a larger drive
or pointing the `DOTNET_WORKLOAD_CACHE_ROOT` environment variable to a location
with more free space before reinstalling workloads.
