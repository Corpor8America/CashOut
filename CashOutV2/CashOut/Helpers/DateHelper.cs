namespace CashOut.Helpers;

public static class DateHelper
{
    public static string MonthName(int month) =>
        new DateOnly(2000, month, 1).ToString("MMMM");
}
