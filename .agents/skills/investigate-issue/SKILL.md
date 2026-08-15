---
name: investigate-issue
description: Use when investigating a GitHub issue by mapping the affected code through the code-memory MCP without executing any implementation work.
---
When investigating a GitHub issue by mapping the affected code through the code-memory MCP.

## Trigger
- `/investigate` followed by issue number, URL, or pasted title/body
- Auto-detects issue references needing investigation

## Workflow
1. **Fetch the issue**: Use GitHub CLI to get issue details if reference provided
2. **Map code via code-memory MCP**:
   - Get issue repository context (owner/repo defaults to current project)
   - Map affected components and symbols through code-memory
   - Trace dependencies upstream/downstream
   - Identify blast radius (affected files, symbols, test coverage)
3. **Ground technical context**:
   - Use microsoft-learn MCP to find official documentation for relevant APIs
   - Search for code samples in relevant technologies
4. **Write findings to docs/investigate-issue-<slug>.md**:
   - Problem context and requirements
   - Symbol → file:line evidence map
   - Blast radius summary
   - Technical constraints and dependencies

## Ground rules
- Read-only investigation only — no branches, commits, or code changes
- All findings must trace to evidence: symbols, file:lines, or microsoft-learn documentation
- UNKNOWN items remain as explicit UNKNOWN markers in findings

## Deliverable format
Path: `docs/investigate-issue-<slug>.md`  
Structure:
```
# Investigation for Issue #<n>

**Requirements**: Explicit and implicit requirements from issue
- ...

**Evidence Map**: 
- Symbol_A → file:line1, file:line2
- Symbol_B → file:line3

**Blast Radius**: 
- Affected files: list
- Test coverage: ... 
- Downstream callers: ...

**Technical Constraints**: 
- ...
```

## Inputs
- Issue reference: number (`#N`), URL, or pasted title/body
- Repository: defaults to current project repo
- Context: issue labels, milestone, comments

## Output
- Plain markdown documentation file in `docs/` directory
- For GitHub issues: path references standard issue tracking URL format
</content>