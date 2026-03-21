# Publish Infinite Scroll

Build, package, and publish a new version of Infinite Scroll.

## Instructions

When the user invokes this skill:

### 1. Determine the new version

- Read the current version from `package.sh` (line 6: `VERSION="x.y.z"`)
- Ask the user what the new version should be, suggesting a patch bump (e.g., 1.0.7 → 1.0.8)
- If the user already specified a version in their message, use that

### 2. Bump the version

- Update `VERSION="x.y.z"` in `package.sh` to the new version

### 3. Commit and push the main repo

- Stage all changes in `/Users/judegao/workspace/personal/infinite-scroll`
- Create a commit with a message summarizing the changes and the new version (e.g., "Fix cell scrolling and v1.0.8")
- Push to origin main

### 4. Build the app

- Run `./package.sh` from the repo root
- Verify the build succeeds and `InfiniteScroll.dmg` is created

### 5. Deploy to the web repo

- Copy the DMG to the web repo:
  ```bash
  cp InfiniteScroll.dmg /Users/judegao/workspace/personal/infinite-scroll-web/public/InfiniteScroll-v{VERSION}.dmg
  ```
- Delete the old versioned DMG from `/Users/judegao/workspace/personal/infinite-scroll-web/public/` (any `InfiniteScroll-v*.dmg` that isn't the new version)
- Update the download link in `/Users/judegao/workspace/personal/infinite-scroll-web/app/page.tsx`:
  - Change `href="/InfiniteScroll-v{OLD}.dmg"` to `href="/InfiniteScroll-v{NEW}.dmg"`

### 6. Commit and push the web repo

- Stage all changes in `/Users/judegao/workspace/personal/infinite-scroll-web`
- Create a commit: `Update to v{VERSION}`
- Push to origin main

### 7. Report

- Print the new version, confirm both repos are pushed
- Remind the user that Vercel will auto-deploy the web repo
