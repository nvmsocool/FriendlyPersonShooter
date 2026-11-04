#!/usr/bin/env bash
# Puts the runner's checkout on the issue branch ($BRANCH), creating it from main if needed.
# Keeps ignored folders such as Library/ so Unity doesn't reimport everything each run.
set -eo pipefail

git config user.name "Claude (issue bot)"
git config user.email "noreply@anthropic.com"

git reset --hard -q
git clean -fdq            # untracked files only; -x would wipe Library/
git fetch -q origin

if git ls-remote --exit-code --heads origin "$BRANCH" >/dev/null; then
  git checkout -q -B "$BRANCH" "origin/$BRANCH"
  # Bring in anything merged to main since; on conflict keep the branch as is.
  git merge -q --no-edit origin/main || { echo "::warning::Could not merge main into $BRANCH"; git merge --abort; }
else
  git checkout -q -B "$BRANCH" origin/main
fi
git log --oneline -1
