namespace AutoPartsErp.SharedKernel.Authorization;

/// <summary>
/// Every permission this system recognizes, in one place.
/// <para>
/// In the shared kernel rather than in the Access module, and it has to be. Access decides who
/// holds a permission; the other eight modules decide which permission each of their routes
/// needs, and they are forbidden from referencing Access's projects — no module in this system
/// references another's. A catalogue both ends compile against is the only shape that works, and
/// it sits next to <c>ICurrentUser</c>, which is here for the same reason.
/// </para>
/// <para>
/// Strings rather than an enum, and the strings are the contract. They go into a token, get
/// stored in a role's row, and are compared by an authorization policy — three places where a
/// renumbered enum member would silently mean something else. A string that no longer exists
/// simply grants nothing, which is the safe direction to fail in.
/// </para>
/// <para>
/// Named <c>module.thing.verb</c>. The shape matters more than it looks: it is what lets a UI
/// group them for a person choosing what a role may do, and what makes an unfamiliar permission
/// readable in a log line six months later.
/// </para>
/// <para>
/// This list is deliberately not generated from the endpoints. A permission is a decision about
/// what a job involves — "may issue documents", "may accept a stock difference" — and endpoints
/// come and go underneath it. Two endpoints sharing one permission is normal and right.
/// </para>
/// </summary>
public static class Permissions
{
    /// <summary>Reading and maintaining the parts catalogue.</summary>
    public static class Catalog
    {
        /// <summary>Look parts up.</summary>
        public const string Read = "catalog.part.read";

        /// <summary>Create and change parts, brands and categories.</summary>
        public const string Manage = "catalog.part.manage";
    }

    /// <summary>Stock, counting it and moving it.</summary>
    public static class Inventory
    {
        /// <summary>See balances, the ledger and what is on order.</summary>
        public const string Read = "inventory.stock.read";

        /// <summary>Book stock in and out against a document.</summary>
        public const string Move = "inventory.stock.move";

        /// <summary>
        /// Correct one balance by hand.
        /// <para>
        /// Separate from moving stock because it is not the same act. A receipt explains itself
        /// through the document behind it; an adjustment is a person overriding the system.
        /// </para>
        /// </summary>
        public const string Adjust = "inventory.stock.adjust";

        /// <summary>Open a count sheet and record what was found on the shelves.</summary>
        public const string Count = "inventory.count.record";

        /// <summary>
        /// Accept the differences on a count sheet and apply them to stock.
        /// <para>
        /// The reason counting and posting are two permissions rather than one: a count that
        /// finds four thousand euros of stock missing is a decision, and the person who walked
        /// the aisle should not have to be the person who signs it off.
        /// </para>
        /// </summary>
        public const string PostCount = "inventory.count.post";

        /// <summary>Raise and send transfers between warehouses.</summary>
        public const string Transfer = "inventory.transfer.manage";

        /// <summary>Set reorder points, warehouses and bins.</summary>
        public const string Configure = "inventory.configure";
    }

    /// <summary>Customers, suppliers and the people at them.</summary>
    public static class Partners
    {
        /// <summary>Look partners up.</summary>
        public const string Read = "partners.partner.read";

        /// <summary>Create and change partners, addresses and contacts.</summary>
        public const string Manage = "partners.partner.manage";

        /// <summary>Set credit limits and put an account on hold.</summary>
        public const string ManageCredit = "partners.credit.manage";
    }

    /// <summary>What the company charges.</summary>
    public static class Pricing
    {
        /// <summary>Ask what a customer pays for something.</summary>
        public const string Quote = "pricing.quote";

        /// <summary>
        /// Read price lists and customer agreements.
        /// <para>
        /// Not the same as quoting, and weaker than maintaining. Quoting answers one question
        /// about one part for one customer; reading a list is every price the company charges,
        /// which is the one document a competitor would most like a copy of.
        /// </para>
        /// </summary>
        public const string Read = "pricing.list.read";

        /// <summary>Create and change price lists, breaks and customer agreements.</summary>
        public const string Manage = "pricing.list.manage";
    }

    /// <summary>Buying.</summary>
    public static class Purchasing
    {
        /// <summary>See purchase orders and the replenishment list.</summary>
        public const string Read = "purchasing.order.read";

        /// <summary>Raise and change purchase orders.</summary>
        public const string Manage = "purchasing.order.manage";

        /// <summary>Send an order to a supplier. The point of commitment.</summary>
        public const string Submit = "purchasing.order.submit";

        /// <summary>Book a delivery in against an order.</summary>
        public const string Receive = "purchasing.receipt.record";
    }

    /// <summary>Selling.</summary>
    public static class Sales
    {
        /// <summary>See orders and customer accounts.</summary>
        public const string Read = "sales.order.read";

        /// <summary>Raise and change sales orders.</summary>
        public const string Manage = "sales.order.manage";

        /// <summary>Confirm an order, which claims stock and tests the credit hold.</summary>
        public const string Confirm = "sales.order.confirm";

        /// <summary>Record that goods went out.</summary>
        public const string Dispatch = "sales.order.dispatch";

        /// <summary>Let an order through a credit hold that would otherwise stop it.</summary>
        public const string OverrideCredit = "sales.credit.override";
    }

    /// <summary>Legal documents.</summary>
    public static class Invoicing
    {
        /// <summary>See documents and drafts.</summary>
        public const string Read = "invoicing.document.read";

        /// <summary>Draw a draft. Nothing here is reported to anybody.</summary>
        public const string Draft = "invoicing.document.draft";

        /// <summary>
        /// Issue a document: number, ATCUD, signature, QR code.
        /// <para>
        /// The most consequential permission in the system. An issued document has been declared
        /// to the tax authority, has VAT falling due against it, and can never be deleted.
        /// </para>
        /// </summary>
        public const string Issue = "invoicing.document.issue";

        /// <summary>Void an issued document, or credit one.</summary>
        public const string Void = "invoicing.document.void";

        /// <summary>Declare a series and record the code the tax authority returns for it.</summary>
        public const string ManageSeries = "invoicing.series.manage";

        /// <summary>Produce the SAF-T (PT) file.</summary>
        public const string ExportSaft = "invoicing.saft.export";
    }

    /// <summary>The sales ledger.</summary>
    public static class Finance
    {
        /// <summary>See what customers owe and since when.</summary>
        public const string Read = "finance.receivable.read";

        /// <summary>Record money arriving and match it to documents.</summary>
        public const string RecordReceipt = "finance.receipt.record";

        /// <summary>Set payment terms.</summary>
        public const string ManageTerms = "finance.terms.manage";
    }

    /// <summary>Who may use the system.</summary>
    public static class Access
    {
        /// <summary>See users and roles.</summary>
        public const string Read = "access.user.read";

        /// <summary>Create users, reset passwords, deactivate accounts.</summary>
        public const string ManageUsers = "access.user.manage";

        /// <summary>
        /// Create roles and decide what they may do.
        /// <para>
        /// Kept apart from managing users because it is a different kind of power. Somebody has
        /// to be able to add a counter clerk without also being able to grant themselves the
        /// right to issue invoices.
        /// </para>
        /// </summary>
        public const string ManageRoles = "access.role.manage";
    }

    /// <summary>
    /// Every permission, for validating a role and for showing somebody the list.
    /// <para>
    /// Written out rather than reflected over the nested classes. Reflection would keep itself up
    /// to date, and would also silently publish anything somebody left half-finished in here.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Catalog.Read, Catalog.Manage,
        Inventory.Read, Inventory.Move, Inventory.Adjust, Inventory.Count, Inventory.PostCount,
        Inventory.Transfer, Inventory.Configure,
        Partners.Read, Partners.Manage, Partners.ManageCredit,
        Pricing.Quote, Pricing.Read, Pricing.Manage,
        Purchasing.Read, Purchasing.Manage, Purchasing.Submit, Purchasing.Receive,
        Sales.Read, Sales.Manage, Sales.Confirm, Sales.Dispatch, Sales.OverrideCredit,
        Invoicing.Read, Invoicing.Draft, Invoicing.Issue, Invoicing.Void,
        Invoicing.ManageSeries, Invoicing.ExportSaft,
        Finance.Read, Finance.RecordReceipt, Finance.ManageTerms,
        Access.Read, Access.ManageUsers, Access.ManageRoles,
    };

    /// <summary>True when the string names a permission this system actually recognizes.</summary>
    public static bool IsKnown(string permission) => All.Contains(permission);
}
