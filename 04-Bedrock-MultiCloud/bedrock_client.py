"""
bedrock_client.py — talk to Claude on Amazon Bedrock through boto3.

Bedrock exposes Claude through `bedrock-runtime`, and the request body is the
Anthropic Messages API shape with two Bedrock-specific differences:

  1. `anthropic_version` is a required body field, always "bedrock-2023-05-31".
     It identifies the Bedrock wire contract, not the model version.
  2. Model IDs carry an `anthropic.` provider prefix — `anthropic.claude-sonnet-5`,
     not `claude-sonnet-5`. The model name never goes in the body; it is the
     `modelId` argument.

Two calls matter:

    invoke_model                     → one JSON response
    invoke_model_with_response_stream → an EventStream of SSE-shaped chunks

Usage
-----
    python bedrock_client.py "Explain prior authorization in two sentences."
    python bedrock_client.py --no-stream "Same question, buffered."
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from typing import Iterator

import boto3
from botocore.config import Config
from botocore.exceptions import ClientError
from dotenv import load_dotenv

load_dotenv()

# Bedrock wire-contract version. Constant — not the Claude model version.
ANTHROPIC_VERSION = "bedrock-2023-05-31"

MODEL_ID = os.getenv("BEDROCK_MODEL_ID", "anthropic.claude-sonnet-5")
REGION = os.getenv("AWS_DEFAULT_REGION", "us-east-1")
MAX_TOKENS = 4096


def make_client(region: str = REGION):
    """bedrock-runtime client for inference calls.

    Note the separate control-plane service: `bedrock` (not `bedrock-runtime`)
    is what you call for list_foundation_models, model access, and provisioned
    throughput. Mixing the two is the most common first-hour mistake.

    boto3 resolves credentials through the standard chain — env vars, shared
    profile, IAM role — so nothing is passed explicitly here. That is what makes
    the same code work locally and on an EC2/Lambda role.
    """
    return boto3.client(
        "bedrock-runtime",
        region_name=region,
        config=Config(
            retries={"max_attempts": 4, "mode": "adaptive"},
            read_timeout=300,  # generous: thinking + long answers stream slowly
        ),
    )


def build_body(prompt: str, system: str | None = None, max_tokens: int = MAX_TOKENS) -> dict:
    """Messages API request body in Bedrock's envelope.

    Sampling parameters are deliberately absent. Current Claude models reject
    `temperature` / `top_p` / `top_k` outright — steer with the system prompt
    instead. Older models accepted them, which is why so much Bedrock sample
    code still sets temperature and now fails.
    """
    body: dict = {
        "anthropic_version": ANTHROPIC_VERSION,
        "max_tokens": max_tokens,
        "messages": [{"role": "user", "content": prompt}],
    }
    if system:
        body["system"] = system
    return body


def invoke(client, prompt: str, system: str | None = None, model_id: str = MODEL_ID) -> str:
    """Single buffered response. Returns the concatenated text blocks."""
    response = client.invoke_model(
        modelId=model_id,
        body=json.dumps(build_body(prompt, system)),
        contentType="application/json",
        accept="application/json",
    )
    payload = json.loads(response["body"].read())

    # Content is a list of typed blocks. Thinking blocks arrive first and carry
    # no text by default, so filtering on type is required — indexing content[0]
    # is the bug that bites everyone who assumed a single text block.
    return "".join(b.get("text", "") for b in payload.get("content", []) if b.get("type") == "text")


def invoke_stream(
    client, prompt: str, system: str | None = None, model_id: str = MODEL_ID
) -> Iterator[str]:
    """Yield text deltas as they arrive.

    The EventStream yields the same event types as the Anthropic SSE stream,
    each wrapped as {"chunk": {"bytes": b"<json>"}}. Only `text_delta` carries
    user-facing text; `thinking_delta` is the model reasoning and is skipped
    here so callers can print the stream straight to a terminal.
    """
    response = client.invoke_model_with_response_stream(
        modelId=model_id,
        body=json.dumps(build_body(prompt, system)),
        contentType="application/json",
        accept="application/json",
    )

    for event in response["body"]:
        chunk = event.get("chunk")
        if not chunk:
            continue
        data = json.loads(chunk["bytes"])

        if data.get("type") == "content_block_delta":
            delta = data.get("delta", {})
            if delta.get("type") == "text_delta":
                yield delta.get("text", "")

        elif data.get("type") == "message_stop":
            # Bedrock attaches invocation metrics here — latency and token
            # counts — which is the cheapest place to hook cost telemetry.
            metrics = data.get("amazon-bedrock-invocationMetrics")
            if metrics:
                print(
                    f"\n\n[{metrics.get('inputTokenCount')} in / "
                    f"{metrics.get('outputTokenCount')} out / "
                    f"{metrics.get('invocationLatency')} ms]",
                    file=sys.stderr,
                )


def explain_error(err: ClientError) -> str:
    """Turn the three Bedrock errors you will actually hit into plain English."""
    code = err.response.get("Error", {}).get("Code", "")
    if code == "AccessDeniedException":
        return (
            "AccessDeniedException — the IAM principal lacks bedrock:InvokeModel, "
            "or model access for this model has not been granted in the Bedrock "
            "console (Model access → Manage model access). Model access is "
            "per-account AND per-region."
        )
    if code == "ValidationException":
        return (
            f"ValidationException — usually a bad modelId for this region, or a "
            f"body field the model rejects (e.g. temperature on current Claude "
            f"models). Model in use: {MODEL_ID}\n{err}"
        )
    if code == "ThrottlingException":
        return "ThrottlingException — on-demand quota exceeded. Retry with backoff or buy provisioned throughput."
    return str(err)


def main() -> int:
    parser = argparse.ArgumentParser(description="Invoke Claude on Amazon Bedrock.")
    parser.add_argument("prompt", nargs="*", help="the prompt to send")
    parser.add_argument("--no-stream", action="store_true", help="buffer the whole response")
    parser.add_argument("--system", default=None, help="optional system prompt")
    parser.add_argument("--model", default=MODEL_ID, help=f"Bedrock model ID (default: {MODEL_ID})")
    args = parser.parse_args()

    if not args.prompt:
        print('Usage: python bedrock_client.py "your prompt"', file=sys.stderr)
        return 1

    prompt = " ".join(args.prompt)
    client = make_client()

    print(f"[{args.model} @ {REGION}]\n", file=sys.stderr)
    try:
        if args.no_stream:
            print(invoke(client, prompt, args.system, args.model))
        else:
            for piece in invoke_stream(client, prompt, args.system, args.model):
                print(piece, end="", flush=True)
            print()
    except ClientError as err:
        print(explain_error(err), file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
