# Amazon Bedrock — Operational Notes

Field notes from standing up Bedrock alongside an existing Azure AI Foundry
deployment. These are the things that cost time.

## Model access is a separate gate from IAM

An IAM policy granting `bedrock:InvokeModel` is necessary but not sufficient.
Each foundation model must additionally be enabled in the Bedrock console under
**Model access**, and that grant is **per account and per region**. A role that
works in `us-east-1` will fail with `AccessDeniedException` in `eu-west-1` until
access is granted there too.

This surprises teams coming from Azure, where deploying a model to a project is
a single step.

## Two services, not one

`bedrock` is the control plane — listing models, managing access, provisioned
throughput, guardrail configuration. `bedrock-runtime` is the data plane —
`invoke_model` and `invoke_model_with_response_stream`. Calling the wrong client
produces a confusing `UnknownServiceError` or a missing-method error rather than
a helpful message.

## Model IDs carry a provider prefix

Bedrock model IDs are namespaced by provider: `anthropic.claude-sonnet-5`,
`amazon.titan-embed-text-v2:0`, `meta.llama3-70b-instruct-v1:0`. Code ported
from a first-party Anthropic integration will fail validation until the
`anthropic.` prefix is added.

Retired models return errors rather than silently falling back. Claude 3 Sonnet
(`anthropic.claude-3-sonnet-20240229-v1:0`) reached end of life on 2025-07-21;
requests against it now fail. Pin model IDs in configuration, not in code, so a
retirement is a config change rather than a redeploy.

## Quotas are per-model, per-region

On-demand throughput quotas are allocated per model per region and are not
shared across models. A workload that saturates one model's quota does not
degrade another. `ThrottlingException` is the signal; the responses are backoff,
a quota increase request, or provisioned throughput for predictable load.

Provisioned throughput is billed hourly whether or not it is used, in model
units purchased for a committed term. It is worth it only above a fairly high
sustained utilization — model the break-even before committing.

## Guardrails apply at invocation, not at the model

Bedrock Guardrails are configured independently of the model and attached per
invocation by ID. The same guardrail can front several models, and a model can
be invoked with or without one. This is more flexible than binding filters to a
deployment, but it means an un-guardrailed call path is easy to introduce by
omission — enforce guardrail IDs in the shared client wrapper, not at each call
site.

## Cost telemetry rides on the response

Streaming responses attach `amazon-bedrock-invocationMetrics` to the terminal
`message_stop` event, carrying input tokens, output tokens, and invocation
latency. Capturing it there is far cheaper than reconciling against Cost
Explorer after the fact, and it gives per-request attribution that billing data
cannot.

## Cross-region inference profiles

Inference profiles route a request across several regions to improve
availability and effective throughput. The trade is data residency: a request
may be served outside the region you called. Any workload with residency
obligations must use a direct regional model ID rather than a profile, and this
must be asserted in tests — it is not visible in the response.
