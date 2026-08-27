public record MonthlyRow(string Month, string Label, decimal Total, int Count);

public record CategoryReportResult(
    int Year, int PreviousYear,
    decimal GrandTotal, decimal PreviousGrandTotal,
    decimal ChangeAmount, decimal ChangePercent,
    int TransactionCount,
    List<CategoryReportRow> Categories);

public record CategoryReportRow(
    string Category,
    decimal Total,
    int Count,
    decimal PctOfSpend,
    decimal PreviousTotal,
    int PreviousCount,
    decimal ChangeAmount,
    decimal ChangePercent,
    List<CategoryTransactionRow> Transactions);

public record CategoryTransactionRow(
    string TransactionId,
    string AccountId,
    DateOnly Date,
    string Name,
    string RawName,
    decimal Amount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    TransactionSource Source);

public record CategoryDetailReportResult(
    int FromYear, int FromMonth, int ToYear, int ToMonth,
    decimal TotalIncome, decimal TotalExpenses, decimal NetCashFlow,
    decimal AvgIncomePerMonth, decimal AvgExpensesPerMonth, decimal AvgNetPerMonth,
    int TransactionCount,
    List<CategoryDetailRow> Categories);

public record CategoryDetailRow(
    string Category, decimal Total, decimal AvgPerMonth,
    int Count, decimal PctOfIncome, decimal PctOfExpenses,
    List<CategoryDetailTransactionRow> Transactions);

public record CategoryDetailTransactionRow(
    string TransactionId, string AccountId, string AccountName,
    DateOnly Date, string Name, string RawName,
    decimal Amount, decimal? Debit, decimal? Credit,
    string Category, TransactionSource Source);

public record CashFlowReportResult(
    int Year, int PreviousYear,
    decimal TotalIncome, decimal TotalExpenses,
    decimal NetCashFlow,
    decimal PreviousYearNet,
    decimal NetChangeAmount, decimal NetChangePercent,
    decimal AverageMonthlyNet,
    decimal BestMonthNet, string BestMonthLabel,
    decimal WorstMonthNet, string WorstMonthLabel,
    int TransactionCount,
    List<CashFlowMonthRow> Months);

public record CashFlowMonthRow(
    string Month, string Label,
    decimal Income, decimal Expenses, decimal Net,
    decimal RollingAverageNet,
    decimal PreviousYearNet,
    decimal ChangeAmount, decimal ChangePercent,
    int IncomeCount, int ExpenseCount, int TransactionCount,
    List<CashFlowTransactionRow> Transactions);

public record CashFlowTransactionRow(
    string TransactionId,
    string AccountId,
    string AccountName,
    DateOnly Date,
    string Name,
    string RawName,
    decimal Amount,
    decimal? Debit,
    decimal? Credit,
    string Category,
    TransactionSource Source);

public record ReportAccountDto(string Id, string Name);

public record AccountDto(Guid Id, string Name, string Description, DateTime CreatedAt);

public record TransactionDto(
    string TransactionId, string AccountId, string AccountName, DateOnly Date,
    string Name, decimal? Credit, decimal? Debit, decimal Amount, string Category,
    int? EffectiveCategoryId, string EffectiveCategoryName, bool IsManualAssignment);

public record CategoryDto(int Id, string Name);

public record RuleDto(int Id, string Pattern, string CategoryName, int CategoryId, int MatchCount);
