# CashOut Reports Specification

This document defines the core reporting system for the CashOut personal finance application. It includes the four foundational reports plus the Executive Summary Dashboard.

## Spending by Category Report

**Purpose**  
Provide a breakdown of where money is being spent across categories for a given period (monthly, quarterly, yearly).

**Data Sources**  
- Transactions  
- Categories  
- Accounts  

**Logic**  
- Include only expense transactions (amount < 0)  
- Group by category  
- Sum absolute value of amounts  
- Compute percent of total spending  
- Compare against previous period  

**Output Fields**  
- Category name  
- Total spend  
- Percent of total  
- Period-over-period change  
- Transaction list  

**Use Cases**  
- Identify overspending  
- Monthly budgeting  
- Category trend analysis  

## Spending by Merchant Report

**Purpose**  
Show which merchants receive the most spending, using normalized merchant names and alias mapping.

**Data Sources**  
- Transactions  
- Merchants  
- Merchant aliases  
- Categories  

**Logic**  
- Normalize merchant using alias table  
- Include only expense transactions  
- Group by merchant  
- Sum absolute value of amounts  
- Include category for each merchant  

**Output Fields**  
- Merchant name  
- Total spend  
- Category  
- Transaction count  
- Trend vs previous period  

**Use Cases**  
- Identify recurring spending  
- Detect rising merchant costs  
- Validate normalization rules  

## Income Report

**Purpose**  
Summarize all income sources and inflows for a given period.

**Data Sources**  
- Transactions  
- Income categories  
- Accounts  

**Logic**  
- Include only income transactions (amount > 0)  
- Group by category or merchant  
- Sum amounts  
- Compare against previous period  

**Output Fields**  
- Income source  
- Total income  
- Percent of total  
- Period-over-period change  
- Transaction list  

**Use Cases**  
- Track salary changes  
- Track freelance income  
- Validate Plaid income categorization  

## Net Cash Flow Report

**Purpose**  
Show whether the user saved or spent more money during a period.

**Data Sources**  
- Transactions  
- Categories  
- Accounts  

**Logic**  
- Income = sum(amount > 0)  
- Expenses = sum(amount < 0)  
- Net cash flow = income – abs(expenses)  
- Compute rolling averages  

**Output Fields**  
- Total income  
- Total expenses  
- Net cash flow  
- Trend line  
- Comparison to previous periods  

**Use Cases**  
- Determine if spending is sustainable  
- Identify negative cash flow months  
- Support long-term planning  

# Executive Summary Dashboard

**Purpose**  
Provide a single-screen overview of the user’s financial status for the current period.

## Monthly Overview  
- Total spending  
- Total income  
- Net cash flow  
- Month-to-month change  

## Top Categories  
- Top 5 spending categories  
- Percent of total  
- Trend indicators  

## Top Merchants  
- Top 5 merchants by spend  
- Category for each  
- Trend indicators  

## Recurring Charges  
- Detected subscriptions  
- Amount changes  
- Upcoming charges  

## Alerts  
- Unmatched merchants  
- Uncategorized transactions  
- Duplicate transaction warnings  
- Rule conflicts  

## Account Summary  
- Account balances  
- Credit card utilization  
- Inflow/outflow per account  
