---
name: dotnet-library-review
description: Use when reviewing a .NET library's API surface as a seasoned senior engineer.
---
Review a .NET library's API surface as a seasoned .NET senior engineer.

## Trigger
- `/library-review` followed by library context or repository path
- Auto-detects library review opportunities based on .NET project structure

## Workflow
1. **Initial assessment**: 
   - Read and analyze README.md for product contract and requirements
   - Scan repository structure to understand the library scope
   - Identify key public APIs and their intended purpose
2. **Code review**: 
   - Analyze code quality, consistency, and adherence to .NET best practices
   - Review API surface design, conventions, and potential improvements
   - Check for missing functionality, edge cases, or potential issues
3. **Documentation check**: 
   - Review README.md, examples, and existing documentation
   - Identify discrepancies between code and documentation
   - Check for clarity, completeness, and accuracy of API documentation
4. **Quality checks**:
   - Run any existing lint/typecheck tools if available
   - Verify build and test success

## Deliverable format
Create `docs/REVIEW-yyyy-mm-dd.md` with:
```
# .NET Library API Review - YYYY-MM-DD

## Overview
- Repository structure
- Primary purpose and scope

## API Surface Analysis
- Public classes, methods, properties
- Usage patterns and examples
- Key design decisions and rationales

## Quality Assessment
### Code Quality
- ...

### Documentation
- ...

### Recommendations
- ...

## GitHub Issues
### Items not yet implemented
- [ ] #NNN — brief description

### Existing issues affecting this review
- #NNN — issue title

## Summary
- ...
```

## Ground rules
- This review is read-only and analytical
- All recommendations and findings should be actionable
- Maintain the tone of a seasoned .NET senior engineer

## Inputs
- Library repository path
- Target framework (.NET 10 in this case)

## Output
- Comprehensive review document in `docs/REVIEW-yyyy-mm-dd.md`
- Formatted findings for easy review and action planning
</content>