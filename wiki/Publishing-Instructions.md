# Publishing Quark Wiki to GitHub

This guide explains how to publish the wiki documentation to GitHub's wiki system.

## Prerequisites

- Git installed on your machine
- Write access to the Quark repository
- GitHub wiki enabled for the repository

## Method 1: Clone and Push (Recommended)

This is the most straightforward method for bulk wiki publishing.

### Step 1: Enable GitHub Wiki

1. Go to your repository on GitHub
2. Navigate to **Settings** → **Features**
3. Check **"Wiki"** to enable it
4. Save changes

### Step 2: Clone the Wiki Repository

GitHub wikis are backed by a separate Git repository:

```bash
# Clone the wiki repository
git clone https://github.com/thnak/Quark.wiki.git

# Navigate into the wiki directory
cd Quark.wiki
```

### Step 3: Copy Wiki Files

Copy all markdown files from the `wiki/` directory:

```bash
# If you're in the Quark.wiki directory and Quark is in the parent directory
cp ../Quark/wiki/*.md .

# Or if you're in the Quark directory
cp wiki/*.md ../Quark.wiki/
cd ../Quark.wiki
```

### Step 4: Commit and Push

```bash
# Add all markdown files
git add *.md

# Commit with a descriptive message
git commit -m "Add comprehensive Quark documentation

- Add Home page with quick links and overview
- Add Getting Started guide
- Add Actor Model documentation
- Add Supervision guide
- Add Persistence documentation
- Add Streaming guide
- Add Clustering documentation
- Add Timers and Reminders guide
- Add Source Generators documentation
- Add API Reference
- Add Examples and patterns
- Add FAQ and troubleshooting
- Add Contributing guidelines"

# Push to GitHub
git push origin master
```

### Step 5: Verify

1. Go to `https://github.com/thnak/Quark/wiki`
2. Check that all pages appear in the sidebar
3. Verify that links between pages work correctly

## Method 2: Automated Script

Create a shell script to automate the process:

### Create publish-wiki.sh

```bash
#!/bin/bash
# publish-wiki.sh - Publish Quark wiki to GitHub

set -e  # Exit on error

REPO_URL="https://github.com/thnak/Quark.wiki.git"
WIKI_DIR="wiki"
TEMP_DIR="temp-wiki"

echo "Publishing Quark wiki to GitHub..."

# Clean up any previous temp directory
if [ -d "$TEMP_DIR" ]; then
    echo "Cleaning up previous temp directory..."
    rm -rf "$TEMP_DIR"
fi

# Clone the wiki repository
echo "Cloning wiki repository..."
git clone "$REPO_URL" "$TEMP_DIR"

# Navigate to temp directory
cd "$TEMP_DIR"

# Remove old files (except .git)
echo "Removing old wiki files..."
find . -maxdepth 1 -type f -name "*.md" -delete

# Copy new wiki files
echo "Copying new wiki files..."
cp ../"$WIKI_DIR"/*.md .

# Check if there are changes
if git diff --quiet && git diff --cached --quiet; then
    echo "No changes to publish."
    cd ..
    rm -rf "$TEMP_DIR"
    exit 0
fi

# Commit changes
echo "Committing changes..."
git add .
git commit -m "Update Quark documentation - $(date '+%Y-%m-%d')"

# Push to GitHub
echo "Pushing to GitHub..."
git push origin master

# Clean up
cd ..
rm -rf "$TEMP_DIR"

echo "✅ Wiki published successfully!"
echo "View at: https://github.com/thnak/Quark/wiki"
```

### Make it Executable and Run

```bash
# Make the script executable
chmod +x publish-wiki.sh

# Run the script
./publish-wiki.sh
```

## Method 3: GitHub Web UI

For updating individual pages:

### Step 1: Navigate to Wiki

1. Go to `https://github.com/thnak/Quark/wiki`
2. If the wiki doesn't exist, click **"Create the first page"**

### Step 2: Create or Edit Pages

For each markdown file:

1. Click **"New Page"** (or edit existing)
2. Enter the page title (e.g., "Getting Started")
3. Copy the content from the corresponding `.md` file
4. Paste into the wiki editor
5. Click **"Save Page"**

### Step 3: Set Home Page

1. Create the "Home" page first
2. GitHub automatically makes it the landing page
3. Ensure the Home page includes navigation to other pages

## Method 4: GitHub CLI

If you have GitHub CLI installed:

```bash
# Install GitHub CLI if not already installed
# https://cli.github.com/

# Clone wiki
gh repo clone thnak/Quark.wiki temp-wiki

# Copy files
cd temp-wiki
cp ../wiki/*.md .

# Commit and push
git add .
git commit -m "Update Quark documentation"
git push

# Clean up
cd ..
rm -rf temp-wiki
```

## Post-Publishing Tasks

### 1. Verify Links

Check that all internal wiki links work:
- Links should use format: `[Link Text](Page-Name)` (without .md)
- Example: `[Getting Started](Getting-Started)`

### 2. Check Navigation

Ensure the sidebar includes all pages:
- GitHub auto-generates sidebar from page titles
- You can customize with a `_Sidebar.md` file if needed

### 3. Test on Mobile

View the wiki on mobile devices to ensure readability.

### 4. Announce the Wiki

Share the wiki with your community:
```markdown
📚 We've published comprehensive documentation!
Check out the new Quark Wiki: https://github.com/thnak/Quark/wiki
```

## Maintaining the Wiki

### Regular Updates

When making code changes:

1. Update the relevant wiki pages in `wiki/` directory
2. Commit to the main repository
3. Re-run the publish script to sync to GitHub wiki

### Version Management

Consider adding version information:
- Tag wiki commits with version numbers
- Add "Last Updated" dates to pages
- Document breaking changes prominently

## Troubleshooting

### "Permission Denied" Error

```bash
# Use SSH instead of HTTPS
git clone git@github.com:thnak/Quark.wiki.git
```

Or configure Git credentials:
```bash
git config --global credential.helper store
```

### "Wiki Not Found" Error

Ensure the wiki is enabled:
1. Go to repository Settings
2. Enable Wiki feature
3. Create at least one page via the web UI
4. Then clone and push

### Links Don't Work

GitHub wiki links should NOT include `.md` extension:
```markdown
✅ Correct: [Actor Model](Actor-Model)
❌ Wrong:   [Actor Model](Actor-Model.md)
```

### Images Not Displaying

For images in the wiki:
1. Upload images via GitHub wiki UI
2. Or reference images from the main repo:
```markdown
![Diagram](https://raw.githubusercontent.com/thnak/Quark/main/docs/images/diagram.png)
```

## Alternative Hosting

If GitHub wiki isn't suitable, consider:

### GitHub Pages
```bash
# Use docs/ folder in main branch
# Enable Pages in repository settings
```

### Read the Docs
```yaml
# Create .readthedocs.yaml
version: 2
mkdocs:
  configuration: mkdocs.yml
```

### Custom Documentation Site
```bash
# Use MkDocs, Docusaurus, or VuePress
# Deploy to Netlify, Vercel, or GitHub Pages
```

## Benefits of GitHub Wiki

✅ **Easy to maintain** - Simple markdown editing  
✅ **Version controlled** - Full Git history  
✅ **Searchable** - GitHub's search indexes wiki  
✅ **Collaborative** - Easy for contributors to update  
✅ **Free hosting** - No cost, included with GitHub  
✅ **Mobile friendly** - Works well on all devices  

## Next Steps

After publishing:

1. ✅ Add a link to the wiki in your main README
2. ✅ Announce in your community channels
3. ✅ Set up regular update schedule
4. ✅ Monitor for feedback and improvements
5. ✅ Consider translations for international users

---

**Questions?** Open an issue in the main repository or start a discussion!

**Wiki URL**: https://github.com/thnak/Quark/wiki
