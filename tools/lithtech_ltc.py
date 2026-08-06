#!/usr/bin/env python3
"""Decode CrossFire's XOR-wrapped LithTech compressed-text (LTC) files."""

from __future__ import annotations

import argparse
import hashlib
from dataclasses import dataclass
from pathlib import Path


# CrossFire repeats this 16-byte mask over the standard LithTech LTC bitstream.
# The bytes form 0x54 + (0x2f * index) modulo 256 for index 0..15.
CROSSFIRE_XOR_KEY = bytes.fromhex("5483b2e1103f6e9dccfb2a5988b7e615")
WINDOW_SIZE = 1 << 12
MIN_SPAN_LENGTH = 2


class LtcDecodeError(ValueError):
    """Raised when an LTC stream cannot be decoded within the safety limits."""


@dataclass(frozen=True)
class LtcDecodeResult:
    data: bytes
    termination: str
    bits_consumed: int
    input_bits: int
    version: int


class _BitReader:
    def __init__(self, data: bytes) -> None:
        self.data = data
        self.position = 0

    def bit(self) -> int:
        if self.position >= len(self.data) * 8:
            raise EOFError
        value = (self.data[self.position // 8] >> (self.position % 8)) & 1
        self.position += 1
        return value

    def unsigned(self, width: int) -> int:
        value = 0
        for _ in range(width):
            value = (value << 1) | self.bit()
        return value


def unwrap_crossfire_ltc(data: bytes) -> bytes:
    """Remove CrossFire's repeating XOR mask without mutating the input."""
    return bytes(value ^ CROSSFIRE_XOR_KEY[index % len(CROSSFIRE_XOR_KEY)] for index, value in enumerate(data))


def decode_ltc(data: bytes, *, maximum_output_bytes: int = 256 * 1024 * 1024) -> LtcDecodeResult:
    """Decode one CrossFire LTC file using the original LithTech LZSS layout.

    Some shipped files contain the explicit offset-zero end token, while others
    end at the physical file boundary. LithTech's reader treats either condition
    as end-of-input, so both are reported explicitly instead of conflated.
    """
    if maximum_output_bytes <= 0:
        raise ValueError("maximum_output_bytes must be positive")
    clear = unwrap_crossfire_ltc(data)
    reader = _BitReader(clear)
    try:
        version = reader.unsigned(32)
    except EOFError as error:
        raise LtcDecodeError("truncated LTC version") from error
    if version != 0:
        raise LtcDecodeError(f"unsupported LTC version: {version}")

    window = bytearray(WINDOW_SIZE)
    window_position = 1
    output = bytearray()

    def append(value: int) -> None:
        nonlocal window_position
        if len(output) >= maximum_output_bytes:
            raise LtcDecodeError(
                f"decoded output exceeds safety limit: {maximum_output_bytes} bytes"
            )
        output.append(value)
        window[window_position] = value
        window_position = (window_position + 1) & (WINDOW_SIZE - 1)

    while True:
        try:
            token_type = reader.bit()
            if token_type:
                append(reader.unsigned(8))
                continue
            span_position = reader.unsigned(12)
            if span_position == 0:
                return LtcDecodeResult(
                    bytes(output), "end_token", reader.position, len(clear) * 8, version
                )
            span_length = reader.unsigned(4) + MIN_SPAN_LENGTH
            for _ in range(span_length):
                value = window[span_position]
                span_position = (span_position + 1) & (WINDOW_SIZE - 1)
                append(value)
        except EOFError:
            return LtcDecodeResult(
                bytes(output), "physical_eof", reader.position, len(clear) * 8, version
            )


def lta_structure(data: bytes) -> dict[str, object]:
    """Return conservative text-shape evidence without claiming semantic validity."""
    depth = 0
    minimum_depth = 0
    in_quote = False
    index = 0
    while index < len(data):
        value = data[index]
        if in_quote:
            if value == 0x5C and index + 1 < len(data):
                index += 2
                continue
            if value == 0x22:
                in_quote = False
        else:
            if value == 0x22:
                in_quote = True
            elif value == 0x2F and index + 1 < len(data) and data[index + 1] == 0x2F:
                newline = data.find(b"\n", index + 2)
                if newline < 0:
                    break
                index = newline
                continue
            elif value == 0x28:
                depth += 1
            elif value == 0x29:
                depth -= 1
                minimum_depth = min(minimum_depth, depth)
        index += 1
    stripped = data.lstrip()
    return {
        "starts_like_lta_text": not data or stripped.startswith((b"(", b"//")),
        "parenthesis_depth": depth,
        "minimum_parenthesis_depth": minimum_depth,
        "unterminated_quote": in_quote,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--maximum-output-bytes", type=int, default=256 * 1024 * 1024)
    args = parser.parse_args()
    source = args.input.read_bytes()
    result = decode_ltc(source, maximum_output_bytes=args.maximum_output_bytes)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    if args.output.exists() and args.output.read_bytes() != result.data:
        raise SystemExit(f"refusing to replace different output: {args.output}")
    args.output.write_bytes(result.data)
    print(
        f"decoded {len(source)} -> {len(result.data)} bytes; "
        f"termination={result.termination}; sha256={hashlib.sha256(result.data).hexdigest()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
