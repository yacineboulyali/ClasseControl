namespace ClasseControle.Common;

/// <summary>
/// Modification élémentaire d'un texte : à la position <see cref="Pos"/>, supprimer
/// <see cref="Del"/> caractères puis insérer <see cref="Ins"/>.
/// Sert à fusionner les saisies simultanées dans l'espace d'équipe
/// (transformation opérationnelle simplifiée, une modification à la fois).
/// </summary>
public readonly record struct TextSplice(int Pos, int Del, string Ins)
{
    public bool IsEmpty => Del == 0 && Ins.Length == 0;

    /// <summary>Calcule la modification qui transforme <paramref name="from"/> en <paramref name="to"/>.</summary>
    public static TextSplice Diff(string from, string to)
    {
        int max = Math.Min(from.Length, to.Length);
        int pre = 0;
        while (pre < max && from[pre] == to[pre]) pre++;
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
    /// Adapte cette modification (écrite sur un texte ancien) pour qu'elle s'applique
    /// après <paramref name="earlier"/>, déjà appliquée par quelqu'un d'autre.
    /// </summary>
    public TextSplice TransformAgainst(TextSplice earlier)
    {
        int b0 = earlier.Pos, b1 = earlier.Pos + earlier.Del, d = earlier.Ins.Length - earlier.Del;
        int Map(int x) => x <= b0 ? x : x >= b1 ? x + d : b0 + earlier.Ins.Length;
        int a0 = Map(Pos), a1 = Map(Pos + Del);
        return new TextSplice(a0, Math.Max(0, a1 - a0), Ins);
    }

    /// <summary>Position du curseur après application de cette modification.</summary>
    public int MapCaret(int caret) =>
        caret <= Pos ? caret : caret >= Pos + Del ? caret + Ins.Length - Del : Pos + Ins.Length;
}
