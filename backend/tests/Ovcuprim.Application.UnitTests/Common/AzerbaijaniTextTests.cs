using Ovcuprim.Application.Common;

namespace Ovcuprim.Application.UnitTests.Common;

public class AzerbaijaniTextSlugTests
{
    [Theory]
    [InlineData("Ovçuluq", "ovculuq")]
    [InlineData("Balıqçılıq", "baliqciliq")]
    [InlineData("Çanta və aksesuar", "canta-ve-aksesuar")]
    [InlineData("Outdoor nəqliyyat", "outdoor-neqliyyat")]
    [InlineData("Bıçaq və alət", "bicaq-ve-alet")]
    [InlineData("Yataq kisəsi", "yataq-kisesi")]
    [InlineData("Ocaq və istilik avadanlığı", "ocaq-ve-istilik-avadanligi")]
    [InlineData("Papaq və əlcək", "papaq-ve-elcek")]
    [InlineData("Müşahidə teleskopu", "musahide-teleskopu")]
    [InlineData("Qəbələ", "qebele")]
    [InlineData("Şəki", "seki")]
    [InlineData("İsmayıllı", "ismayilli")]
    [InlineData("Lənkəran", "lenkeran")]
    public void Builds_the_expected_slug(string input, string expected)
    {
        Assert.Equal(expected, AzerbaijaniText.ToSlug(input));
    }

    [Theory]
    [InlineData("Ə", "e")]
    [InlineData("ə", "e")]
    [InlineData("ı", "i")]
    [InlineData("ş", "s")]
    [InlineData("ç", "c")]
    [InlineData("ğ", "g")]
    [InlineData("ö", "o")]
    [InlineData("ü", "u")]
    public void Folds_every_azerbaijani_letter(string input, string expected)
    {
        Assert.Equal(expected, AzerbaijaniText.ToSlug(input));
    }

    [Fact]
    public void Handles_both_azerbaijani_i_pairs()
    {
        // I is the capital of dotless ı; İ is the capital of dotted i. Both fold to plain i,
        // which an invariant ToLower would get wrong for one of the pairs.
        Assert.Equal("i", AzerbaijaniText.ToSlug("I"));
        Assert.Equal("i", AzerbaijaniText.ToSlug("ı"));
        Assert.Equal("i", AzerbaijaniText.ToSlug("İ"));
        Assert.Equal("i", AzerbaijaniText.ToSlug("i"));
    }

    [Theory]
    [InlineData("  Çadır  ", "cadir")]
    [InlineData("Qayıq / kayak", "qayiq-kayak")]
    [InlineData("ATV və digər", "atv-ve-diger")]
    [InlineData("---", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Trims_collapses_and_survives_empty_input(string? input, string expected)
    {
        Assert.Equal(expected, AzerbaijaniText.ToSlug(input));
    }

    [Fact]
    public void Never_produces_leading_trailing_or_doubled_hyphens()
    {
        var slug = AzerbaijaniText.ToSlug("  ///Ov   avadanlıqları!!!  ");

        Assert.Equal("ov-avadanliqlari", slug);
        Assert.True(AzerbaijaniText.IsSlug(slug));
    }

    [Fact]
    public void Truncates_to_the_maximum_length()
    {
        Assert.Equal("karavan-treyl", AzerbaijaniText.ToSlug("Karavan treyler aksesuarları", 13));
    }

    [Fact]
    public void Truncation_never_leaves_a_trailing_hyphen()
    {
        // Cutting at 8 would land exactly on the separator.
        var slug = AzerbaijaniText.ToSlug("Karavan treyler aksesuarları", 8);

        Assert.Equal("karavan", slug);
        Assert.True(AzerbaijaniText.IsSlug(slug));
    }

    [Fact]
    public void Every_seeded_style_name_yields_a_valid_slug()
    {
        string[] names =
        [
            "Ovçuluq", "Balıqçılıq", "Kamp", "Outdoor geyim", "Çanta və aksesuar",
            "Outdoor nəqliyyat", "Optika və durbin", "Bıçaq və alət",
            "Ov avadanlıqları", "Tilovlar", "Yataq kisəsi", "Kamp mətbəxi",
            "Termal geyim", "Yağış geyimi", "Off-road aksesuarları", "Müşahidə teleskopu"
        ];

        Assert.All(names, n => Assert.True(AzerbaijaniText.IsSlug(AzerbaijaniText.ToSlug(n)), n));
    }
}

public class AzerbaijaniTextNormalizeTests
{
    [Theory]
    [InlineData("Şimano", "simano")]
    [InlineData("KARBON", "karbon")]
    [InlineData("Çadır 4 nəfərlik", "cadir 4 neferlik")]
    [InlineData("  boşluq   çox  ", "bosluq cox")]
    public void Folds_and_lowercases_while_keeping_word_boundaries(string input, string expected)
    {
        Assert.Equal(expected, AzerbaijaniText.Normalize(input));
    }

    [Fact]
    public void Returns_empty_for_missing_input()
    {
        Assert.Equal(string.Empty, AzerbaijaniText.Normalize(null));
        Assert.Equal(string.Empty, AzerbaijaniText.Normalize("   "));
    }

    [Fact]
    public void Normalising_a_search_term_matches_the_normalised_title()
    {
        // The point of the shared normaliser: a buyer typing without diacritics still matches.
        Assert.Equal(
            AzerbaijaniText.Normalize("Karbon spinning tilov"),
            AzerbaijaniText.Normalize("karbon spinninq tilov".Replace("spinninq", "spinning")));

        Assert.Equal("baliqciliq", AzerbaijaniText.Normalize("Balıqçılıq"));
        Assert.Equal("baliqciliq", AzerbaijaniText.Normalize("baliqciliq"));
    }
}

public class SlugValidationTests
{
    [Theory]
    [InlineData("ovculuq", true)]
    [InlineData("canta-ve-aksesuar", true)]
    [InlineData("atv-2", true)]
    [InlineData("Ovculuq", false)]
    [InlineData("-ovculuq", false)]
    [InlineData("ovculuq-", false)]
    [InlineData("ov--culuq", false)]
    [InlineData("ovçuluq", false)]
    [InlineData("ov culuq", false)]
    [InlineData("", false)]
    public void Recognises_valid_slugs(string value, bool expected)
    {
        Assert.Equal(expected, AzerbaijaniText.IsSlug(value));
    }
}
