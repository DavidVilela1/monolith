using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Domain;

/// <summary>
/// Every failure the Catalog module can report, in one place.
/// <para>
/// Collecting them here rather than building error strings at the throw site means the codes
/// are stable, greppable and translatable, and it makes the module's failure surface something
/// you can read in one sitting.
/// </para>
/// </summary>
public static class CatalogErrors
{
    /// <summary>Failures relating to a <see cref="Parts.Part"/>.</summary>
    public static class Part
    {
        /// <summary>The part does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("catalog.part.not_found", $"No part matches '{identifier}'.");

        /// <summary>A part with this SKU already exists.</summary>
        public static Error SkuAlreadyExists(string sku) =>
            Error.Conflict("catalog.part.sku_exists", $"SKU '{sku}' is already in use.");

        /// <summary>This brand already has a part with this manufacturer part number.</summary>
        public static Error PartNumberAlreadyExists(string number) =>
            Error.Conflict(
                "catalog.part.number_exists",
                $"This brand already has a part with number '{number}'.");

        /// <summary>A SKU is required.</summary>
        public static readonly Error SkuRequired =
            Error.Validation("catalog.part.sku_required", "A SKU is required.");

        /// <summary>The SKU is too long.</summary>
        public static readonly Error SkuTooLong =
            Error.Validation(
                "catalog.part.sku_too_long",
                $"A SKU may be at most {Parts.Sku.MaxLength} characters.");

        /// <summary>The SKU contains characters that are not allowed.</summary>
        public static readonly Error SkuInvalidCharacters =
            Error.Validation(
                "catalog.part.sku_invalid",
                "A SKU may contain only letters, digits, hyphen, dot, slash and underscore, and must start with a letter or digit.");

        /// <summary>A manufacturer part number is required.</summary>
        public static readonly Error PartNumberRequired =
            Error.Validation("catalog.part.number_required", "A manufacturer part number is required.");

        /// <summary>The manufacturer part number is too long.</summary>
        public static readonly Error PartNumberTooLong =
            Error.Validation(
                "catalog.part.number_too_long",
                $"A part number may be at most {Parts.PartNumber.MaxLength} characters.");

        /// <summary>The manufacturer part number contains no letters or digits.</summary>
        public static readonly Error PartNumberInvalid =
            Error.Validation(
                "catalog.part.number_invalid",
                "A part number must contain at least one letter or digit.");

        /// <summary>A name is required.</summary>
        public static readonly Error NameRequired =
            Error.Validation("catalog.part.name_required", "A part description is required.");

        /// <summary>The name is too long.</summary>
        public static readonly Error NameTooLong =
            Error.Validation("catalog.part.name_too_long", "A part description may be at most 200 characters.");

        /// <summary>A brand is required.</summary>
        public static readonly Error BrandRequired =
            Error.Validation("catalog.part.brand_required", "A part must belong to a brand.");

        /// <summary>A category is required.</summary>
        public static readonly Error CategoryRequired =
            Error.Validation("catalog.part.category_required", "A part must belong to a category.");

        /// <summary>Weight cannot be negative.</summary>
        public static readonly Error WeightNegative =
            Error.Validation("catalog.part.weight_negative", "Weight cannot be negative.");

        /// <summary>Dimensions cannot be negative.</summary>
        public static readonly Error DimensionNegative =
            Error.Validation("catalog.part.dimension_negative", "Dimensions cannot be negative.");

        /// <summary>Dangerous goods need a UN number.</summary>
        public static readonly Error DangerousGoodsNeedUnNumber =
            Error.Validation(
                "catalog.part.un_number_required",
                "A part flagged as dangerous goods must carry a UN number.");

        /// <summary>The core charge must be greater than zero.</summary>
        public static readonly Error CoreChargeMustBePositive =
            Error.Validation(
                "catalog.part.core_charge_invalid",
                "A core charge must be greater than zero.");

        /// <summary>A part sold against a core must have a core charge before it goes live.</summary>
        public static readonly Error CoreChargeRequired =
            Error.DomainRule(
                "catalog.part.core_charge_missing",
                "A part sold against a returnable core needs a core charge before it can be activated.");

        /// <summary>The stocking unit can no longer be changed.</summary>
        public static readonly Error StockUnitLocked =
            Error.DomainRule(
                "catalog.part.stock_unit_locked",
                "The stocking unit can only be changed while a part is still a draft, because stock quantities and open orders are recorded in it.");

        /// <summary>A part that has been live cannot go back to draft.</summary>
        public static readonly Error CannotReactivate =
            Error.DomainRule(
                "catalog.part.cannot_reactivate",
                "Only a draft part can be activated. A discontinued part stays discontinued.");

        /// <summary>A draft part was never sold, so there is nothing to discontinue.</summary>
        public static readonly Error CannotDiscontinueDraft =
            Error.DomainRule(
                "catalog.part.cannot_discontinue_draft",
                "A draft part has never been sold. Delete it instead of discontinuing it.");

        /// <summary>A draft part cannot be made obsolete.</summary>
        public static readonly Error CannotObsoleteDraft =
            Error.DomainRule(
                "catalog.part.cannot_obsolete_draft",
                "A draft part has no history to preserve. Delete it instead.");

        /// <summary>An obsolete part is frozen.</summary>
        public static readonly Error ObsoleteIsReadOnly =
            Error.DomainRule(
                "catalog.part.obsolete_readonly",
                "An obsolete part is kept only so historical documents still resolve, and cannot be changed.");

        /// <summary>A part cannot supersede itself.</summary>
        public static readonly Error CannotSupersedeItself =
            Error.DomainRule(
                "catalog.part.supersedes_itself",
                "A part cannot be its own replacement.");

        /// <summary>The superseding part reference is not usable.</summary>
        public static readonly Error SupersessionInvalid =
            Error.Validation(
                "catalog.part.supersession_invalid",
                "The superseding part reference is not valid.");
    }

    /// <summary>Failures relating to a <see cref="Parts.CrossReference"/>.</summary>
    public static class CrossReference
    {
        /// <summary>A cross-reference kind must be specified.</summary>
        public static readonly Error KindRequired =
            Error.Validation(
                "catalog.cross_reference.kind_required",
                "Specify why the numbers are linked: OEM, competitor, supersession, interchange or trading partner.");

        /// <summary>The same cross-reference is already recorded.</summary>
        public static readonly Error Duplicate =
            Error.Conflict(
                "catalog.cross_reference.duplicate",
                "That cross-reference is already recorded against this part.");

        /// <summary>A part cannot cross-reference its own number.</summary>
        public static readonly Error SameAsOwnNumber =
            Error.DomainRule(
                "catalog.cross_reference.same_as_own",
                "A part cannot cross-reference its own manufacturer part number.");

        /// <summary>The cross-reference is not recorded against this part.</summary>
        public static readonly Error NotFound =
            Error.NotFound(
                "catalog.cross_reference.not_found",
                "That cross-reference is not recorded against this part.");
    }

    /// <summary>Failures relating to a <see cref="Parts.Fitment"/>.</summary>
    public static class Fitment
    {
        /// <summary>The vehicle make is required.</summary>
        public static readonly Error MakeRequired =
            Error.Validation("catalog.fitment.make_required", "A vehicle make is required.");

        /// <summary>The vehicle model is required.</summary>
        public static readonly Error ModelRequired =
            Error.Validation("catalog.fitment.model_required", "A vehicle model is required.");

        /// <summary>The model year is outside the accepted range.</summary>
        public static readonly Error YearOutOfRange =
            Error.Validation(
                "catalog.fitment.year_out_of_range",
                $"Model years must fall between {Parts.Fitment.EarliestYear} and two years from now.");

        /// <summary>The year range runs backwards.</summary>
        public static readonly Error YearRangeInverted =
            Error.Validation(
                "catalog.fitment.year_range_inverted",
                "The last model year cannot be earlier than the first.");

        /// <summary>The same vehicle application is already recorded.</summary>
        public static readonly Error Duplicate =
            Error.Conflict(
                "catalog.fitment.duplicate",
                "That vehicle application is already recorded against this part.");

        /// <summary>The vehicle application is not recorded against this part.</summary>
        public static readonly Error NotFound =
            Error.NotFound(
                "catalog.fitment.not_found",
                "That vehicle application is not recorded against this part.");
    }

    /// <summary>Failures relating to a <see cref="Brands.Brand"/>.</summary>
    public static class Brand
    {
        /// <summary>The brand does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("catalog.brand.not_found", $"No brand matches '{identifier}'.");

        /// <summary>A brand with this code already exists.</summary>
        public static Error CodeAlreadyExists(string code) =>
            Error.Conflict("catalog.brand.code_exists", $"Brand code '{code}' is already in use.");

        /// <summary>A brand code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("catalog.brand.code_required", "A brand code is required.");

        /// <summary>The brand code is too long.</summary>
        public static readonly Error CodeTooLong =
            Error.Validation("catalog.brand.code_too_long", "A brand code may be at most 20 characters.");

        /// <summary>A brand name is required.</summary>
        public static readonly Error NameRequired =
            Error.Validation("catalog.brand.name_required", "A brand name is required.");

        /// <summary>The brand name is too long.</summary>
        public static readonly Error NameTooLong =
            Error.Validation("catalog.brand.name_too_long", "A brand name may be at most 120 characters.");

        /// <summary>The brand is not active.</summary>
        public static readonly Error Inactive =
            Error.DomainRule("catalog.brand.inactive", "That brand is no longer active.");
    }

    /// <summary>Failures relating to a <see cref="Categories.PartCategory"/>.</summary>
    public static class Category
    {
        /// <summary>The category does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("catalog.category.not_found", $"No category matches '{identifier}'.");

        /// <summary>A category with this code already exists.</summary>
        public static Error CodeAlreadyExists(string code) =>
            Error.Conflict("catalog.category.code_exists", $"Category code '{code}' is already in use.");

        /// <summary>A category code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("catalog.category.code_required", "A category code is required.");

        /// <summary>The category code is too long.</summary>
        public static readonly Error CodeTooLong =
            Error.Validation("catalog.category.code_too_long", "A category code may be at most 20 characters.");

        /// <summary>A category name is required.</summary>
        public static readonly Error NameRequired =
            Error.Validation("catalog.category.name_required", "A category name is required.");

        /// <summary>The category name is too long.</summary>
        public static readonly Error NameTooLong =
            Error.Validation("catalog.category.name_too_long", "A category name may be at most 120 characters.");

        /// <summary>A category cannot be its own parent.</summary>
        public static readonly Error CannotParentItself =
            Error.DomainRule("catalog.category.parent_cycle", "A category cannot be its own parent.");
    }
}
