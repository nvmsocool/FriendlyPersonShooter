#!/usr/bin/env bash
# Prints an issue and all its comments as Markdown, for Claude's prompt.
# Comments posted by the workflow itself are labeled as Claude's own.
set -eo pipefail
gh issue view "$1" --json number,title,body,author,comments --template '
# Issue thread

## #{{.number}}: {{.title}}

**@{{.author.login}}** opened the issue:

{{.body}}
{{range .comments}}
---
{{if eq .author.login "github-actions"}}**You (Claude)** wrote{{else}}**@{{.author.login}}** wrote{{end}}:

{{.body}}
{{end}}'
