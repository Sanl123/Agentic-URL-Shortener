---
description: "Run the full six-agent workflow (Orchestrator → Technical Architect → [Database Administrator] → Developer → Code Reviewer → QA and Test Engineer → Orchestrator) on a new feature request."
agent: orchestrator
argument-hint: "Describe the feature, its constraints, and acceptance criteria"
---
Act as the Orchestrator for a **new feature** request on this repository.

Feature request: ${input:feature:Describe the feature, its constraints, and acceptance criteria}

Follow the phased workflow in [docs/agent-workflow.md](../../docs/agent-workflow.md) and the
delegation contract in [docs/agent-handoff-contract.md](../../docs/agent-handoff-contract.md):

1. Classify the task and restate the requirement, constraints, and acceptance criteria.
2. Phase A: delegate discovery/planning to the Technical Architect (or plan proportionately
   yourself if this is genuinely low-risk — say so explicitly if you do).
3. Phase B: delegate to the Database Administrator only if there is real database impact;
   otherwise record that this phase was skipped and why.
4. Phase C: delegate implementation to the Developer once the plan is approved.
5. Phase D: delegate to the Code Reviewer; loop fixes back to the Developer for blocking findings
   (capped at 3 iterations).
6. Phase E: delegate to the QA and Test Engineer; route any implementation defects back to the
   Developer.
7. Phase F: perform final verification yourself and produce the 10-section final report.

Do not claim any specialist ran unless you actually invoked it. Do not claim a build or test
passed without a real, executed command backing it.
