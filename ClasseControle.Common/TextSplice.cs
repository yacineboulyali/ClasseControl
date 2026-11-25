namespace ClasseControle.Common;

/// <summary>
/// Modification élémentaire d'un texte : à la position <see cref="Pos"/>, supprimer
/// <see cref="Del"/> caractères puis insérer <see cref="Ins"/>.
/// Base de l'édition à plusieurs de l'espace d'équipe (transformation opérationnelle).
/// </summary>
public readonly record struct TextSplice(int Pos, int Del, string Ins)
{
    public bool IsEmpty => Del == 0 && Ins.Length == 0;

    /// <summary>Calcule une modification qui transforme <paramref name="from"/> en <paramref name="to"/>.</summary>
    public static TextSplice Diff(string from, string to) => Diff(from, to, -1);

    /// <summary>
    /// Idem, en levant les ambiguïtés (lettres répétées, ex. « ee ») grâce à la position du
    /// curseur après la frappe : le texte saisi se termine au curseur.
    /// </summary>
    public static TextSplice Diff(string from, string to, int caretAfter)
    {
        int max = Math.Min(from.Length, to.Length);
        int pre = 0;
        while (pre < max && from[pre] == to[pre]) pre++;
        if (caretAfter >= 0)
        {
            // Le changement se termine au plus tard au curseur : la partie commune ne le dépasse pas.
            int growth = Math.Max(0, to.Length - from.Length);
            pre = Math.Min(pre, Math.Max(0, caretAfter - growth));
        }
        int suf = 0;
        while (suf < max - pre && from[from.Length - 1 - suf] == to[to.Length - 1 - suf]) suf++;
        return new TextSplice(pre, from.Length - pre - suf, to.Substring(pre, to.Length - pre - suf));
    }

    public string ApplyTo(string text)
    {
        int pos = Math.Clamp(Pos, 0, text.Length);
        int del = Math.Clamp(Del, 0, text.Length - pos);
        return text.Remove(pos, del).Insert(pos, Ins);
    }

    /// <summary>
    /// Adapte cette modification pour qu'elle s'applique après <paramref name="other"/>,
    /// faite en parallèle sur le même texte. <paramref name="thisFirst"/> départage deux
    /// insertions au même endroit (celle qui passe devant). Une modification entièrement
    /// située dans un passage supprimé par l'autre disparaît (la suppression l'emporte).
    /// </summary>
    public TextSplice TransformAgainst(TextSplice other, bool thisFirst)
    {
        int b0 = other.Pos, b1 = other.Pos + other.Del, n = other.Ins.Length, d = n - other.Del;
        int a0 = Pos, a1 = Pos + Del;

        // Entièrement à l'intérieur d'un passage supprimé par l'autre : rien ne subsiste.
        if (other.Del > 0 && a0 > b0 && a1 < b1) return new TextSplice(b0 + n, 0, "");

        int s;
        if (a0 < b0) s = a0;
        else if (a0 == b0) s = thisFirst ? a0 : a0 + n;
        else if (a0 >= b1) s = a0 + d;
        else s = b0 + n;
        if (Del == 0) return new TextSplice(s, 0, Ins);

        int e;
        if (a1 <= b0) e = a1;
        else if (a1 >= b1) e = a1 + d;
        else e = b0;
        // Une suppression qui englobe la modification de l'autre emporte aussi son insertion.
        if (a0 < b0 && a1 >= b1) e = a1 + d;
        return new TextSplice(s, Math.Max(0, e - s), Ins);
    }

    /// <summary>Compatibilité : l'autre modification est prioritaire en cas d'égalité.</summary>
    public TextSplice TransformAgainst(TextSplice earlier) => TransformAgainst(earlier, thisFirst: true);

    /// <summary>Position d'un curseur après application de cette modification.</summary>
    public int MapCaret(int caret) =>
        caret <= Pos ? caret : caret >= Pos + Del ? caret + Ins.Length - Del : Pos + Ins.Length;
}

/// <summary>Forme sérialisable d'une <see cref="TextSplice"/> (réseau).</summary>
public sealed class SpliceDto
{
    public int Pos { get; set; }
    public int Del { get; set; }
    public string Ins { get; set; } = "";
    public static SpliceDto From(TextSplice s) => new() { Pos = s.Pos, Del = s.Del, Ins = s.Ins };
    public TextSplice ToSplice() => new(Pos, Del, Ins ?? "");
}
