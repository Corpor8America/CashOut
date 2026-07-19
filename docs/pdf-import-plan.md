# PDF Import Integration Plan

## Overview
Add PDF upload support to the existing CSV import flow. When a PDF is uploaded, the server extracts text with **PdfPig**, parses transactions using heuristics (date/amount detection), generates a CSV string, and feeds it into the existing column mapping and import pipeline.

## Changes

### 1. Add PdfPig NuGet package
**File:** `CashOut/CashOut.csproj`
- Add `<PackageReference Include="PdfPig" Version="0.1.15" />`

### 2. New service: `CashOut/Services/PdfImportService.cs`
A new scoped service that handles PDF text extraction and transaction parsing.

**Methods:**
- `string ExtractCsv(byte[] pdfBytes)` — main entry point
  1. Opens PDF with PdfPig, extracts text from all pages using `ContentOrderTextExtractor.GetText(page)` (layout-aware, proper reading order)
  2. Detects year from PDF text (search for 4-digit year patterns near "Statement Period", "Account Summary", etc.; fall back to current year)
  3. Splits extracted text into lines
  4. For each line, tries to identify a transaction:
     - **Date detection**: multiple formats — `MM/DD`, `MM/DD/YYYY`, `M/D/YYYY`, `YYYY-MM-DD`, `Mon DD, YYYY`, `DD Mon YYYY`
     - **Amount detection**: `$X.XX`, `-$X.XX`, `($X.XX)`, `X,XXX.XX-` (credit card parenthetical notation)
     - If both date and amount found → transaction row. Everything between date and amount = description.
  5. Skips non-transaction lines (headers, section labels like "Payments", "General Purchases", totals, blank lines)
  6. Outputs CSV string: `Date,Description,Amount` with one row per transaction

**Output sign convention** (matches CashOut): positive = expense, negative = income/inflow. So `-$45.67` from the PDF becomes `-45.67` in the CSV.

**Year auto-detection logic:**
- Scan extracted text for patterns like `Statement Period.*(\d{4})`, `Account Summary.*(\d{4})`, or any 4-digit year in range 2020-2030
- If found → use that year for `MM/DD`-only dates
- If not found → use current year

### 3. Add endpoint to `CashOut/Controllers/CsvImportController.cs`
**New endpoint:** `POST api/csv-import/{accountId}/pdf-preview`
- Receives `[FromForm] IFormFile file`
- Reads PDF bytes, calls `PdfImportService.ExtractCsv()`
- Passes the generated CSV to existing `CsvImportService.Preview()` 
- Returns the same `CsvPreview` format the client already expects

This means the client gets back `{ Headers: ["Date","Description","Amount"], Rows: [[...], ...] }` — identical to what a CSV upload would return.

### 4. Update `CashOut/Pages/CsvImport.razor`
- **`<InputFile>` accept**: change from `.csv` to `.csv,.pdf`
- **Drop zone text**: "Drag & drop your CSV or PDF here"
- **`ProcessFile()`**: detect file extension
  - `.csv` → existing flow (read into string, call preview)
  - `.pdf` → send raw bytes to `pdf-preview` endpoint, get CSV string back, store in `_csvContent`, auto-configure mapping:
    - `DateColumn = "Date"`
    - `DescriptionColumn = "Description"`
    - `AmountColumn = "Amount"`
    - `_amountType = "single"`
    - `SkipRowsFromTop = 0`, `SkipRowsFromBottom = 0`
- After PDF preview loads, user sees the parsed transactions in the same mapping UI and can adjust if needed
- **Import step**: unchanged — `_csvContent` contains CSV, existing import endpoint handles it

### 5. Register service in `CashOut/Program.cs`
- Add `builder.Services.AddScoped<PdfImportService>();`

## File summary
| File | Change |
|---|---|
| `CashOut/CashOut.csproj` | Add PdfPig 0.1.15 |
| `CashOut/Services/PdfImportService.cs` | **New** — PDF extraction + parsing |
| `CashOut/Controllers/CsvImportController.cs` | Add `pdf-preview` endpoint |
| `CashOut/Pages/CsvImport.razor` | Accept `.pdf`, detect format, auto-configure mapping |
| `CashOut/Program.cs` | Register `PdfImportService` |

## What stays the same
- `CsvImportService` — untouched, PDF-derived CSV goes through the same pipeline
- `CsvMappingProfile` — auto-configured for PDF, user can tweak
- Import/dedup/normalization — unchanged
- Skipped rows export — unchanged

## Error handling
- PDF with no extractable text (scanned/image) → return error message
- No transactions found → return warning with raw text for inspection
- PdfPig exceptions → return descriptive error to client

## Limitations
- Scanned/image-only PDFs won't work (PdfPig can only read text-based PDFs)
- Multi-line transactions may not parse correctly (heuristic assumes one transaction per line)
- The reference ID becomes part of the description (harmless, can be cleaned up via merchant normalization)
