# ADR-0004: Own narrow LLM port over the native Anthropic SDK; model tiers

- Status: Accepted
- Date: 2026-10-06

## Context
- The app needs a few specific LLM capabilities: structured JSON output, PDF/text documents as input,
  later tool calling (agent loop), token and cost tracking.
- A learning goal of the project is to understand these mechanics (requests, tokens, tool use, agent loop) first-hand.
- Measured on real data: Claude Haiku 4.5 did not reliably follow nuanced evaluation rules (stereotypes in risks,
  language errors); Claude Sonnet 5.5 did, at ~2.7× the cost (~$0.013 per evaluated job ad).

## Decision
- A small application port `ILlmClient` (`LlmRequest` → `LlmResponse` with text, model, stop reason, tokens),
  implemented by `ClaudeLlmClient` on the **official native Anthropic C# SDK**.
- Requests ask for a **tier** (`Fast` / `Smart`), not a model name; concrete models are configuration
  (`Claude:Models:Fast/Smart`), validated at start-up. Smart = Sonnet for profile and evaluation,
  Fast = Haiku for cheap high-volume triage.

## Alternatives considered
- **Microsoft.Extensions.AI `IChatClient`** – good portable abstraction, but its automatic function invocation
  hides the agent loop this project wants to build explicitly. May be adopted later behind the same port.
- **Semantic Kernel / LangChain-style frameworks** – much larger surface than needed; harder to see what is sent.
- **Using the SDK directly in application code** – couples every use case to one vendor and makes tests need the network.

## Consequences
- Switching provider or model = one adapter / one config value.
- Use cases are testable with a fake `ILlmClient`.
- Features of the SDK that are not exposed through the port (streaming, batches, caching) have to be added
  to the port deliberately when needed.
