---
name: reference-analyzer
description: Analyze completed video performance data to improve future video guidance. Use when the user says things like "analyze my videos", "update insights", "run the reference analyzer", or "improve the guidance". Periodic meta-level tool.
---

## Purpose

Takes completed video data points and outcome data, analyzes patterns, and outputs opinionated insight files that improve long form and short form workflows over time.

Does NOT run as part of every video creation. It is a meta-level tool.

## Status: TODO

Stubbed and ready for implementation. Core workflows run on hardcoded guidance until this is built.

### What needs to be designed

1. Video data point template format
2. Analyzer workflow (pattern detection)
3. Output format for insight banks

### Design principles

- Output only actionable insights — filter bad patterns
- Never touch core guidance files
- Insight files are owned by the analyzer
- Be opinionated: specific observations with outcomes

### When to build

Implement when there are at least 3–5 completed videos with data points. To start: invoke and say "let's design the video data template."
