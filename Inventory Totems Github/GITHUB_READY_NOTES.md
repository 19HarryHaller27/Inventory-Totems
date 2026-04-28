# GitHub Ready Notes

This folder is now prepared to be used directly as a GitHub repository root.

Included for publishing:

- source: `src/`
- assets: `assets/`
- mod metadata: `modinfo.json`
- project/build files: `InventoryTotems.csproj`, `Directory.Build.props`
- documentation: `README.md`, `WEBSITE_COPY.md`, `AUDIT.md`
- ignore rules: `.gitignore`

## Quick publish

1. Open this folder in terminal.
2. `git init`
3. `git add .`
4. `git commit -m "Initial Inventory Totems publish"`
5. Create remote repo on GitHub.
6. `git remote add origin <your-repo-url>`
7. `git branch -M main`
8. `git push -u origin main`
