using Ventia.Application.Common;
using Ventia.Domain.Entities;

namespace Ventia.Tests.Unit;

public class DocumentsTests
{
    [Theory]
    [InlineData(DocumentType.CC, "1.053.123.456", "1053123456")]
    [InlineData(DocumentType.CC, " 75 000 111 ", "75000111")]
    [InlineData(DocumentType.CE, "ab12345", "AB12345")]
    [InlineData(DocumentType.PP, "pa123456", "PA123456")]
    [InlineData(DocumentType.NIT, "800.197.268-4", "800197268-4")]
    public void Valid_documents_are_normalized(DocumentType type, string input, string expected) =>
        Assert.Equal(expected, Documents.Normalize(type, input));

    [Theory]
    [InlineData(DocumentType.CC, "12")]
    [InlineData(DocumentType.CC, "12345678901")]
    [InlineData(DocumentType.CC, "12-345")]
    [InlineData(DocumentType.CE, "A1")]
    [InlineData(DocumentType.PP, "PA-123")]
    [InlineData(DocumentType.NIT, "800197268-5")]
    [InlineData(DocumentType.NIT, "")]
    public void Invalid_documents_are_rejected(DocumentType type, string input) =>
        Assert.Null(Documents.Normalize(type, input));

    [Fact]
    public void Only_people_need_data_consent()
    {
        Assert.True(Documents.IsPerson(DocumentType.CC));
        Assert.True(Documents.IsPerson(DocumentType.PP));
        Assert.False(Documents.IsPerson(DocumentType.NIT));
    }
}
