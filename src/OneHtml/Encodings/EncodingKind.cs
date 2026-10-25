namespace RecursiveAI.OneHtml.Encodings;

/// <summary>Which decoder algorithm an encoding uses.</summary>
internal enum EncodingKind
{
    Utf8,
    SingleByte,
    Gbk,
    Gb18030,
    Big5,
    EucJp,
    Iso2022Jp,
    ShiftJis,
    EucKr,
    Replacement,
    Utf16Be,
    Utf16Le,
    XUserDefined,
}
