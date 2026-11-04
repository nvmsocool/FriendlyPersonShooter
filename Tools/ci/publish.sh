#!/usr/bin/env bash
# After Claude's change is built: commit, push the issue branch, open/update the PR,
# upload the zipped build as a pre-release, and post the download link on the issue.
# Needs: ISSUE, BRANCH, GH_TOKEN, RUNNER_TEMP (commit-msg.txt, summary.md), and the zip from build.ps1.
set -eo pipefail

zip="Builds/FriendlyPersonShooter-issue-$ISSUE.zip"
run_url="$GITHUB_SERVER_URL/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID"
title=$(gh issue view "$ISSUE" --json title --jq .title)
msg=$(cat "$RUNNER_TEMP/commit-msg.txt")

git add -A
if git diff --cached --quiet; then
  gh issue comment "$ISSUE" --body "🤖 I finished without changing any files, so there's no new build. Details: $run_url"
  gh issue edit "$ISSUE" --add-label needs-info --remove-label in-progress || true
  exit 0
fi
git commit -q -F - <<EOF
$msg

Issue #$ISSUE

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
git push -q -u origin "$BRANCH"
sha=$(git rev-parse HEAD)
short=${sha:0:7}

# Pull request (one per issue; later rounds add commits to it).
pr=$(gh pr list --head "$BRANCH" --state open --json url --jq '.[0].url')
if [ -z "$pr" ]; then
  {
    echo "Closes #$ISSUE"
    echo
    cat "$RUNNER_TEMP/summary.md"
    echo
    echo "🤖 Generated with [Claude Code](https://claude.com/claude-code)"
  } > "$RUNNER_TEMP/pr.md"
  pr=$(gh pr create --base main --head "$BRANCH" --title "$title" --body-file "$RUNNER_TEMP/pr.md")
fi

# Build download as a pre-release.
tag="issue-$ISSUE-$short"
asset="FriendlyPersonShooter-issue-$ISSUE-$short.zip"
cp "$zip" "$RUNNER_TEMP/$asset"
gh release create "$tag" "$RUNNER_TEMP/$asset" --prerelease --target "$sha" \
  --title "Build for #$ISSUE: $title ($short)" --notes-file "$RUNNER_TEMP/summary.md"
download="$GITHUB_SERVER_URL/$GITHUB_REPOSITORY/releases/download/$tag/$asset"

{
  echo "### 🤖 Build ready: [**Download $asset**]($download)"
  echo
  cat "$RUNNER_TEMP/summary.md"
  echo
  echo "---"
  echo "Unzip it and run \`FriendlyPersonShooter.exe\`. Code changes: $pr"
  echo "_Want tweaks? Comment \`@claude <what to change>\` for a new plan, then \`@claude go\`. The owner merges the PR when it's good._"
} > "$RUNNER_TEMP/comment.md"
gh issue comment "$ISSUE" --body-file "$RUNNER_TEMP/comment.md"
gh issue edit "$ISSUE" --add-label build-ready --remove-label in-progress || true

# Keep only the 10 newest issue builds.
gh release list --limit 200 --json tagName,createdAt \
  --jq '[.[] | select(.tagName | startswith("issue-"))] | sort_by(.createdAt) | reverse | .[10:] | .[].tagName' |
  while read -r old; do gh release delete "$old" --yes --cleanup-tag || true; done
