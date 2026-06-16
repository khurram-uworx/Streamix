# Proposal: Hybrid AI Classification Strategy for Personalized RSS Intelligence Platform

## Problem Statement

We want to build a personalized content intelligence platform where users provide:

* RSS feeds
* Topics of interest ("signals")
* Feedback on relevance

The system should:

1. Filter noise
2. Categorize content
3. Learn user interests over time
4. Minimize LLM cost while maintaining accuracy

---

# Core Idea

Use a hybrid classification approach:

1. LLMs bootstrap the system and classify uncertain content.
2. Embeddings and vector search classify high-confidence content.
3. User feedback continuously improves relevance.

The goal is to progressively reduce LLM usage while maintaining classification quality.

---

# High-Level Pipeline

```text
RSS Feed
    ↓
Normalize Content
    ↓
Deduplicate
    ↓
Generate Embedding
    ↓
Vector-Based Classification
    ↓
Confidence Check
    ↓
    ├─ High Confidence → Auto Label
    │
    └─ Low Confidence → LLM Classification
                              ↓
                     Store Result + Embedding
```

---

# Why Not Use LLMs For Everything?

Using an LLM for every item is expensive and slow.

Example:

```text
1000 RSS items/day
200 user signals

Potentially thousands of LLM evaluations
```

Instead:

```text
Vector Search
    ↓
Only uncertain cases reach the LLM
```

Expected outcome:

```text
80-95% items classified without LLM
5-20% items classified by LLM
```

---

# Learning Process

## First Item

```text
Item #1
    ↓
LLM Classifies
    ↓
Store Label + Embedding
```

Example:

```text
Item #1 → Red
```

---

## Second Item

```text
Generate Embedding
Compare to Existing Items
```

If similarity is high:

```text
Item #2
Similarity to Item #1 = 0.92
```

We may still use the LLM during early bootstrap.

---

## As More Data Arrives

After some time:

```text
Red = 50 items
Blue = 30 items
Green = 20 items
```

Every item now has:

* Label
* Embedding

This becomes our training corpus.

---

# Classification Strategy

When Item #101 arrives:

```text
Generate Embedding
Find Similar Items
```

Example:

```text
Nearest Neighbors

Red  0.91
Red  0.89
Red  0.88
Red  0.87
Blue 0.65
```

Result:

```text
High confidence
Auto-label as Red
No LLM call
```

---

# Confidence-Based Decision Making

Important:

We do NOT rely on item counts.

We rely on confidence.

Bad:

```text
If category has >20 items
    Use vectors
```

Good:

```text
If confidence > threshold
    Use vectors
Else
    Use LLM
```

---

# Confidence Signals

## 1. Similarity Strength

How close is the item to existing examples?

Example:

```text
0.91 = very strong
0.85 = good
0.50 = weak
```

---

## 2. Neighbor Agreement

Do nearby items agree?

Example:

```text
Red
Red
Red
Red
Red
```

Strong agreement.

Example:

```text
Red
Blue
Red
Green
Blue
```

Weak agreement.

---

## 3. Margin Between Categories

Example:

```text
Red   0.91
Blue  0.72
Green 0.69
```

Strong winner.

Example:

```text
Red   0.81
Blue  0.79
Green 0.77
```

Uncertain.

Use LLM.

---

# Example Confidence Rule

```text
Auto-classify if:

Average Similarity >= 0.84

AND

At least 5 nearest neighbors agree

AND

Top category exceeds second category by 0.08
```

Otherwise:

```text
Send to LLM
```

---

# Handling New Topics

Example:

```text
Red   0.50
Blue  0.30
Green 0.30
```

Although Red is highest:

```text
0.50 is weak similarity
```

Decision:

```text
Use LLM
```

Possible outcome:

```text
New Category
Noise
Existing Category
```

This allows discovery of emerging topics without forcing content into existing labels.

---

# Category Centroids

In addition to item-to-item similarity, we can maintain category centroids.

A centroid is:

```text
Average Embedding
Of All Items
Within A Category
```

Example:

```text
Red Centroid
Blue Centroid
Green Centroid
```

New items are compared against:

1. Category centroid
2. Similar historical items

If both agree:

```text
Auto-label
```

If not:

```text
Use LLM
```

---

# Bootstrap Phase

Suggested approach:

First 20-50 items:

```text
Always call LLM
```

At the same time:

```text
Calculate vector predictions
Compare against LLM result
Measure agreement
```

Example:

```text
Vector → Red
LLM    → Red
```

Track accuracy over time.

Once agreement becomes consistently high:

```text
Enable auto-classification
```

---

# User Feedback Loop

User actions become training signals.

Examples:

```text
Like
Save
Hide
More Like This
Less Like This
```

These actions can:

* Adjust ranking
* Adjust category confidence
* Improve future recommendations

---

# Expected Benefits

## Reduced Cost

Most items eventually avoid LLM classification.

## Faster Processing

Vector similarity is significantly faster than LLM inference.

## Adaptive Learning

The system becomes better as more content is processed.

## New Topic Discovery

Low-confidence items are routed to the LLM, enabling category evolution.

---

# Recommendation

Adopt a hybrid architecture:

```text
Vectors = Fast Path

LLM = Expert Review Path
```

The system should classify using vector similarity whenever confidence is sufficiently high and only escalate uncertain content to the LLM.

This provides a scalable, cost-effective, and continuously improving content intelligence platform.
 
