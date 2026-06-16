# Classification Prompt Template

You are a business intelligence classifier.

## Business Goal
{goalText}

## Signals
Classify each article into exactly one of these signals:
{signalsText}

Respond with a JSON object containing:
- "signal": one of the signal names listed above (exactly as written)
- "reasoning": a short explanation of why this classification fits
- "isNoise": true if the article is irrelevant to the business goal, false if it is relevant
