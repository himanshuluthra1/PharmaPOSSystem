using PharmaPOS.Application.Features.Reports;
using PharmaPOS.Shared.Constants;

namespace PharmaPOS.Application.Features.Settings;

/// <summary>Maps <see cref="ReportKind"/> to assignable menu permission keys.</summary>
public static class ReportMenuPermissions
{
    public static string For(ReportKind kind) => kind switch
    {
        ReportKind.Sales => AppConstants.Permissions.ReportsMenuSales,
        ReportKind.SalesByCustomer => AppConstants.Permissions.ReportsMenuSalesByCustomer,
        ReportKind.SalesByMedicine => AppConstants.Permissions.ReportsMenuSalesByMedicine,
        ReportKind.SalesByPaymentMode => AppConstants.Permissions.ReportsMenuSalesByPaymentMode,
        ReportKind.SalesDayWise => AppConstants.Permissions.ReportsMenuSalesDayWise,
        ReportKind.SalesCreditDue => AppConstants.Permissions.ReportsMenuSalesCreditDue,
        ReportKind.Profit => AppConstants.Permissions.ReportsMenuProfit,
        ReportKind.SaleReturns => AppConstants.Permissions.ReportsMenuSaleReturns,
        ReportKind.MedicineReturns => AppConstants.Permissions.ReportsMenuMedicineReturns,
        ReportKind.ScheduleRegister => AppConstants.Permissions.ReportsMenuScheduleRegister,
        ReportKind.Purchases => AppConstants.Permissions.ReportsMenuPurchases,
        ReportKind.PurchasesBySupplier => AppConstants.Permissions.ReportsMenuPurchasesBySupplier,
        ReportKind.SupplierOutstanding => AppConstants.Permissions.ReportsMenuSupplierOutstanding,
        ReportKind.SupplierPayments => AppConstants.Permissions.ReportsMenuSupplierPayments,
        ReportKind.PurchaseReturns => AppConstants.Permissions.ReportsMenuPurchaseReturns,
        ReportKind.ExpiryToCompanyClaims => AppConstants.Permissions.ReportsMenuExpiryToCompanyClaims,
        ReportKind.CustomerOutstanding => AppConstants.Permissions.ReportsMenuCustomerOutstanding,
        ReportKind.CustomerReceipts => AppConstants.Permissions.ReportsMenuCustomerReceipts,
        ReportKind.PaymentVouchers => AppConstants.Permissions.ReportsMenuPaymentVouchers,
        ReportKind.ReceiptVouchers => AppConstants.Permissions.ReportsMenuReceiptVouchers,
        ReportKind.ExpenseRegister => AppConstants.Permissions.ReportsMenuExpenseRegister,
        ReportKind.ExpenseByAccount => AppConstants.Permissions.ReportsMenuExpenseByAccount,
        ReportKind.CashBookSummary => AppConstants.Permissions.ReportsMenuCashBookSummary,
        ReportKind.GstSummary => AppConstants.Permissions.ReportsMenuGstSummary,
        ReportKind.Gstr1 => AppConstants.Permissions.ReportsMenuGstr1,
        ReportKind.Gstr2B => AppConstants.Permissions.ReportsMenuGstr2B,
        ReportKind.StockValuation => AppConstants.Permissions.ReportsMenuStockValuation,
        ReportKind.BatchStock => AppConstants.Permissions.ReportsMenuBatchStock,
        ReportKind.Expiry => AppConstants.Permissions.ReportsMenuExpiry,
        ReportKind.LowStock => AppConstants.Permissions.ReportsMenuLowStock,
        ReportKind.SlowMovingStock => AppConstants.Permissions.ReportsMenuSlowMovingStock,
        ReportKind.StockAdjustments => AppConstants.Permissions.ReportsMenuStockAdjustments,
        ReportKind.MedicinesSoldByDate => AppConstants.Permissions.ReportsMenuMedicinesSoldByDate,
        _ => AppConstants.Permissions.ReportsView
    };

    public static IReadOnlyList<string> AllKeys { get; } =
    [
        AppConstants.Permissions.ReportsMenuSales,
        AppConstants.Permissions.ReportsMenuSalesByCustomer,
        AppConstants.Permissions.ReportsMenuSalesByMedicine,
        AppConstants.Permissions.ReportsMenuSalesByPaymentMode,
        AppConstants.Permissions.ReportsMenuSalesDayWise,
        AppConstants.Permissions.ReportsMenuSalesCreditDue,
        AppConstants.Permissions.ReportsMenuProfit,
        AppConstants.Permissions.ReportsMenuSaleReturns,
        AppConstants.Permissions.ReportsMenuMedicineReturns,
        AppConstants.Permissions.ReportsMenuScheduleRegister,
        AppConstants.Permissions.ReportsMenuPurchases,
        AppConstants.Permissions.ReportsMenuPurchasesBySupplier,
        AppConstants.Permissions.ReportsMenuSupplierOutstanding,
        AppConstants.Permissions.ReportsMenuSupplierPayments,
        AppConstants.Permissions.ReportsMenuPurchaseReturns,
        AppConstants.Permissions.ReportsMenuExpiryToCompanyClaims,
        AppConstants.Permissions.ReportsMenuCustomerOutstanding,
        AppConstants.Permissions.ReportsMenuCustomerReceipts,
        AppConstants.Permissions.ReportsMenuPaymentVouchers,
        AppConstants.Permissions.ReportsMenuReceiptVouchers,
        AppConstants.Permissions.ReportsMenuExpenseRegister,
        AppConstants.Permissions.ReportsMenuExpenseByAccount,
        AppConstants.Permissions.ReportsMenuCashBookSummary,
        AppConstants.Permissions.ReportsMenuGstSummary,
        AppConstants.Permissions.ReportsMenuGstr1,
        AppConstants.Permissions.ReportsMenuGstr2B,
        AppConstants.Permissions.ReportsMenuStockValuation,
        AppConstants.Permissions.ReportsMenuBatchStock,
        AppConstants.Permissions.ReportsMenuExpiry,
        AppConstants.Permissions.ReportsMenuLowStock,
        AppConstants.Permissions.ReportsMenuSlowMovingStock,
        AppConstants.Permissions.ReportsMenuStockAdjustments,
        AppConstants.Permissions.ReportsMenuMedicinesSoldByDate,
    ];
}
