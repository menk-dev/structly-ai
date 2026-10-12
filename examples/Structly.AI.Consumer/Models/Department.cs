using System.ComponentModel;
using System.Text.Json.Serialization;

public enum Department
{
    [Description("Payments, invoices, and refunds."), JsonStringEnumMemberName("billing")]
    Billing,
    [Description("Problems using the product.")]
    Technical,
}
