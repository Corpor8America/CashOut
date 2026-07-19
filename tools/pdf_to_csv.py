#!/usr/bin/env python3
"""
Extract transactions from credit card statement PDFs and output a CSV
compatible with the CashOut CSV import (Date, Description, Amount).

Usage:
    python tools/pdf_to_csv.py statement.pdf
    python tools/pdf_to_csv.py statement.pdf -o output.csv
    python tools/pdf_to_csv.py statement.pdf --year 2025
    python tools/pdf_to_csv.py statement.pdf --raw        # dump raw extracted text
    python tools/pdf_to_csv.py statement.pdf --debug      # show per-line parse results
"""

import argparse
import csv
import re
import sys
from datetime import datetime
from pathlib import Path

try:
    import pdfplumber
except ImportError:
    sys.exit(
        "pdfplumber is required. Install it with:\n"
        "  pip install pdfplumber"
    )


# ── Patterns ─────────────────────────────────────────────────────────────────

# Transaction line: MM/DD  REF#  DESCRIPTION  $AMOUNT or -$AMOUNT
# Example: 05/22 2469216GYBPMK3WAE FOOD LION #0040 CARY NC $45.67
TXN_RE = re.compile(
    r"^(\d{1,2}/\d{1,2})\s+"           # date: MM/DD
    r"(\S+)\s+"                         # reference #
    r"(.+?)\s+"                         # description (greedy, last space+amount wins)
    r"(-?\$[\d,]+\.\d{2})\s*$"         # amount: $X.XX or -$X.XX
)

# Section headers to skip: "Payments -$1,166.73" or "General Purchases and Other Debits $1,272.73"
SECTION_HEADER_RE = re.compile(
    r"^(Payments|General Purchases|Debits|Credits|Interest|Fees|"
    r"New Balance|Previous Balance|Credit Limit|Available Credit|"
    r"Account Number|Statement Period|Payment Due Date|Total Minimum)"
    r"\b", re.IGNORECASE
)

# Table header row to skip
TABLE_HEADER_RE = re.compile(
    r"^\s*Date\s+.*Reference\s+.*Description\s+.*Amount", re.IGNORECASE
)

# Amount with dollar sign
DOLLAR_AMT_RE = re.compile(r"-?\$[\d,]+\.\d{2}")

# Parenthesized amount: ($123.45)
PAREN_AMT_RE = re.compile(r"\(\s*\$?\s*([\d,]+\.?\d*)\s*\)")


# ── Helpers ──────────────────────────────────────────────────────────────────

def parse_amount(raw: str) -> float | None:
    """Parse a dollar amount string into a float. Negative if prefixed with '-'."""
    raw = raw.strip()

    # Parenthesized = negative
    pm = PAREN_AMT_RE.search(raw)
    if pm:
        val = pm.group(1).replace(",", "")
        try:
            return -float(val)
        except ValueError:
            return None

    # Strip $ and commas
    cleaned = raw.replace("$", "").replace(",", "").strip()
    if cleaned.startswith("-"):
        try:
            return -float(cleaned[1:])
        except ValueError:
            return None
    try:
        return float(cleaned)
    except ValueError:
        return None


def extract_text(pdf_path: str, page: int = 0) -> str:
    """Extract text from PDF. page=0 means all pages."""
    with pdfplumber.open(str(pdf_path)) as pdf:
        if page > 0:
            if page > len(pdf.pages):
                sys.exit(f"Page {page} does not exist (PDF has {len(pdf.pages)} pages)")
            return pdf.pages[page - 1].extract_text() or ""
        pages = [p.extract_text() for p in pdf.pages]
        return "\n".join(t for t in pages if t)


def extract_transactions(text: str, year: int, debug: bool = False) -> list[tuple[datetime, str, float]]:
    """Parse transactions from PDF text."""
    transactions = []
    lines = text.split("\n")

    for i, line in enumerate(lines):
        raw = line.strip()

        # Skip blanks, headers, section labels
        if not raw:
            continue
        if TABLE_HEADER_RE.match(raw):
            continue
        if SECTION_HEADER_RE.match(raw):
            continue

        m = TXN_RE.match(raw)
        if not m:
            if debug and len(raw) > 3:
                print(f"  [skip] {raw[:120]}", file=sys.stderr)
            continue

        date_str, _ref, description, amount_str = m.groups()

        # Parse date
        try:
            month, day = date_str.split("/")
            dt = datetime(year, int(month), int(day))
        except (ValueError, AttributeError):
            if debug:
                print(f"  [skip-date] {raw[:120]}", file=sys.stderr)
            continue

        # Parse amount
        amount = parse_amount(amount_str)
        if amount is None or amount == 0:
            if debug:
                print(f"  [skip-amt] {raw[:120]}", file=sys.stderr)
            continue

        description = description.strip()
        if not description:
            description = "Unknown"

        transactions.append((dt, description, amount))

    return transactions


def write_csv(transactions: list[tuple[datetime, str, float]], output_path: str | None):
    """Write transactions to CSV in CashOut-compatible format."""
    if output_path:
        f = open(output_path, "w", newline="", encoding="utf-8")
    else:
        f = sys.stdout

    writer = csv.writer(f)
    writer.writerow(["Date", "Description", "Amount"])

    for date, desc, amount in transactions:
        writer.writerow([
            date.strftime("%m/%d/%Y"),
            desc,
            f"{amount:.2f}",
        ])

    if output_path:
        f.close()


def main():
    parser = argparse.ArgumentParser(
        description="Extract transactions from credit card statement PDFs to CSV."
    )
    parser.add_argument("pdf", help="Path to the PDF statement")
    parser.add_argument("-o", "--output", help="Output CSV path (default: <input>.csv)")
    parser.add_argument("--year", type=int, default=datetime.now().year,
                        help=f"Year to use for dates (default: {datetime.now().year})")
    parser.add_argument("--raw", action="store_true",
                        help="Dump raw extracted text and exit")
    parser.add_argument("--debug", action="store_true",
                        help="Show lines that were skipped during parsing")
    parser.add_argument("--page", type=int, default=0,
                        help="Extract only a specific page (1-indexed, 0 = all)")
    args = parser.parse_args()

    pdf_path = Path(args.pdf)
    if not pdf_path.exists():
        sys.exit(f"File not found: {pdf_path}")

    text = extract_text(str(pdf_path), args.page)

    if args.raw:
        print(text)
        return

    transactions = extract_transactions(text, args.year, debug=args.debug)

    if not transactions:
        print("No transactions found in PDF.", file=sys.stderr)
        print("Try --raw to see the extracted text and check the format.", file=sys.stderr)
        sys.exit(1)

    transactions.sort(key=lambda t: t[0])

    output = args.output or str(pdf_path.with_suffix(".csv"))
    write_csv(transactions, output)
    print(f"\nExtracted {len(transactions)} transactions -> {output}", file=sys.stderr)


if __name__ == "__main__":
    main()
