using System.IO.Compression;
using System.Text;
using Demo.Api.AI.Knowledge;
using Demo.Api.Errors;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
namespace Demo.Api.Tests;
public sealed class KnowledgeExtractionTests
{
    [Fact]
    public void Docx_extraction_preserves_paragraphs_and_Turkish_text_without_writes() {
        using var memory=new MemoryStream();
        using(var doc=WordprocessingDocument.Create(memory,DocumentFormat.OpenXml.WordprocessingDocumentType.Document,true)) {
            var main=doc.AddMainDocumentPart();main.Document=new Document(new Body(new Paragraph(new Run(new Text("Sipariş onayından önce miktarları kontrol edin.")))));main.Document.Save();
        }
        var parts=new DocumentExtractor(new()).Extract(memory.ToArray(),"manual.docx",CancellationToken.None);Assert.Contains("Sipariş onayından",Assert.Single(parts).Text);
    }
    [Fact]
    public void Pdf_text_and_page_reference_are_extracted_from_real_pdf_bytes() {
        var builder=new PdfDocumentBuilder();var font=builder.AddStandard14Font(Standard14Font.Helvetica);var page=builder.AddPage(400,600);
        page.AddText("Orders require authorized confirmation.",12,new PdfPoint(30,500),font);
        var sections=new DocumentExtractor(new()).Extract(builder.Build(),"manual.pdf",CancellationToken.None);
        Assert.Equal("Page 1",Assert.Single(sections).Reference);Assert.Contains("Orders",sections[0].Text);
    }
    [Fact]
    public void Image_only_pdf_is_explicitly_marked_as_requiring_OCR() {
        var builder=new PdfDocumentBuilder();builder.AddPage(400,600);
        var error=Assert.Throws<BusinessException>(()=>new DocumentExtractor(new()).Extract(builder.Build(),"scanned.pdf",CancellationToken.None));Assert.Equal("knowledgeOcrRequired",error.Code);
    }
    [Theory]
    [InlineData("/Encrypt")][InlineData("/JavaScript")][InlineData("/Launch")]
    public void Pdf_encryption_and_executable_features_are_rejected(string feature) {
        var bytes=Encoding.ASCII.GetBytes("%PDF-1.7 "+feature);var error=Assert.Throws<BusinessException>(()=>new DocumentExtractor(new()).Extract(bytes,"bad.pdf",CancellationToken.None));Assert.Equal("knowledgeInvalidFile",error.Code);
    }
    [Fact]
    public void Docx_embedded_macros_and_external_relationships_are_rejected() {
        foreach(var dangerous in new[]{"word/vbaProject.bin","word/embeddings/file.bin","word/_rels/document.xml.rels"}) {
            using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)) {
                using(var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open()))writer.Write("<document/>");
                using(var writer=new StreamWriter(zip.CreateEntry(dangerous).Open()))writer.Write("<Relationship TargetMode=\"External\" />");
            }
            Assert.Throws<BusinessException>(()=>new DocumentExtractor(new()).Extract(memory.ToArray(),"bad.docx",CancellationToken.None));
        }
    }
    [Fact]
    public void Text_limits_binary_content_and_invalid_UTF8_are_rejected() {
        var extractor=new DocumentExtractor(new(){MaxFileBytes=1024,MaxCharacters=100});
        foreach(var bytes in new[]{new byte[1025],new byte[]{0xff,0xff},Encoding.UTF8.GetBytes(new string('x',101))})Assert.Throws<BusinessException>(()=>extractor.Extract(bytes,"bad.txt",CancellationToken.None));
    }
    [Fact]
    public void Extraction_cancellation_is_honored() {
        using var token=new CancellationTokenSource();token.Cancel();var extractor=new DocumentExtractor(new());
        Assert.Throws<OperationCanceledException>(()=>extractor.Extract(Encoding.UTF8.GetBytes("# Heading\nText"),"manual.md",token.Token));
    }
}
