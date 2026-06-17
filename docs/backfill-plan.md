# Transaction Backfill Plan

This document outlines the strategy for backfilling historical transactions into a linked (Plaid) account using CSV files, while ensuring data integrity by preventing duplicates.

## 1. Goal
Provide a way for users to import data that falls outside of the date range supported by Plaid's API sync, while automatically handling overlapping "boundary" dates.

## 2. Implementation Strategy: Direct CSV Import
We will extend the existing CSV import functionality to support Linked Account IDs.

### UI Changes
*   **Linked Accounts Page**: Add a **"Backfill (CSV)"** button to each account row. This will link to the CSV import page, passing the specific `AccountId`.
*   **Import Result Page**: Update the summary to distinguish between:
    *   **Internal Duplicates**: Matches found within the current CSV file being uploaded.
    *   **Cross-Source Duplicates**: Matches found between the CSV file and transactions already in the database (from Plaid or previous imports).

## 3. Deduplication: "Smart Fingerprint" Check
Since Plaid and CSV sources do not share a common unique identifier, we will use a "fuzzy" fingerprint to identify duplicates.

### Fingerprint Logic
A CSV row is considered a duplicate of an existing transaction if it meets **all** of the following criteria:
1.  **Exact Date Match**: The transaction dates must be identical.
2.  **Exact Amount Match**: The currency amount must be identical (including the positive/negative sign).
3.  **Name Similarity**:
    *   Both names are normalized (stripped of punctuation, converted to uppercase).
    *   One name contains the other (e.g., `WALMART` is a match for `WALMART STORE 123`).

### Process Flow
During the `Import` method in `CsvImportService`:
1.  Fetch all existing transactions for the target `AccountId` that fall within the date range of the CSV being uploaded.
2.  For each row in the CSV:
    *   Compare it against the fetched transactions using the "Smart Fingerprint."
    *   If a match is found, skip the row and increment the **Cross-Source Duplicates** counter.
    *   If no match is found, create the transaction as usual.

## 4. Risks and Mitigation
*   **False Positives**: Two identical transactions on the same day (e.g., two $5.00 coffees). 
    *   *Mitigation*: This is rare in backfilling old data, and automated deduplication provides a better user experience than manual cleanup of hundreds of overlaps.
*   **False Negatives**: The bank uses vastly different names in the CSV vs. Plaid.
    *   *Mitigation*: The fuzzy name match (Contains) catches most common variations.

## 5. Next Steps
1.  Modify `CsvImportService` to implement the fingerprint check.
2.  Update `Accounts.razor` UI.
3.  Update `CsvImport.razor` UI to display the new duplicate statistics.
