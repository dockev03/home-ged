using HomeGed.Api.Shares;

namespace HomeGed.Api.Tests;

public sealed class SharePasswordHasherTests
{
    [Fact]
    public void Empreinte_argon2id_au_format_PHC_sans_le_mot_de_passe()
    {
        var hash = SharePasswordHasher.Hash("Correct-Cheval-42");

        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", hash, StringComparison.Ordinal);
        Assert.DoesNotContain("Correct-Cheval-42", hash, StringComparison.Ordinal);
        Assert.Equal(6, hash.Split('$').Length);
    }

    [Fact]
    public void Verifie_le_bon_mot_de_passe_seulement()
    {
        var hash = SharePasswordHasher.Hash("Correct-Cheval-42");

        Assert.True(SharePasswordHasher.Verify("Correct-Cheval-42", hash));
        Assert.False(SharePasswordHasher.Verify("correct-cheval-42", hash));
        Assert.False(SharePasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Sel_aleatoire_deux_empreintes_differentes()
    {
        Assert.NotEqual(SharePasswordHasher.Hash("même mot de passe"), SharePasswordHasher.Hash("même mot de passe"));
    }

    [Fact]
    public void Accents_et_emojis_acceptes()
    {
        var hash = SharePasswordHasher.Hash("Élise🔒été");

        Assert.True(SharePasswordHasher.Verify("Élise🔒été", hash));
        Assert.False(SharePasswordHasher.Verify("Elise🔒ete", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("texte")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$c2VsLWRlLXRlc3Q$aGFzaA")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$c2VsLWRlLXRlc3Q")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$!!!$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=99999999,t=2,p=1$c2VsLWRlLXRlc3Q$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=19456,t=99,p=1$c2VsLWRlLXRlc3Q$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=19456,t=2,x=1$c2VsLWRlLXRlc3Q$aGFzaGhhc2hoYXNoaGFzaA")]
    public void Empreinte_illisible_ou_hors_bornes_refusee(string encoded)
    {
        Assert.False(SharePasswordHasher.Verify("Correct-Cheval-42", encoded));
    }

    [Fact]
    public void Empreinte_alteree_refusee()
    {
        var hash = SharePasswordHasher.Hash("Correct-Cheval-42");
        var last = hash[^2] == 'A' ? 'B' : 'A';

        Assert.False(SharePasswordHasher.Verify("Correct-Cheval-42", hash[..^2] + last + hash[^1]));
    }
}

public sealed class ShareLinkIdsTests
{
    [Fact]
    public void Identifiant_de_128_bits_en_base64url()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => ShareLinkIds.New()).ToList();

        Assert.All(ids, id =>
        {
            Assert.Equal(22, id.Length);
            Assert.Matches("^[A-Za-z0-9_-]{22}$", id);
            Assert.True(ShareLinkIds.IsValid(id));
        });
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("../../../../etc/passwd")]
    public void Forme_invalide_refusee(string? id)
    {
        Assert.False(ShareLinkIds.IsValid(id));
    }
}
