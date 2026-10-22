using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Demo.Api.Errors;
using UglyToad.PdfPig;
namespace Demo.Api.AI.Knowledge;

public interface IDocumentExtractor { List<ExtractedSection> Extract(byte[] data,string filename,CancellationToken ct); }
public interface IKnowledgeChunker { List<TextChunk> Split(List<ExtractedSection> sections); }
public sealed class DocumentExtractor(KnowledgeOptions options):IDocumentExtractor
{
    public List<ExtractedSection> Extract(byte[] data,string filename,CancellationToken ct) {
        if(data.Length==0||data.Length>options.MaxFileBytes)throw new BusinessException("knowledgeFileSize",400);
        var extension=Path.GetExtension(filename).ToLowerInvariant();var result=new List<ExtractedSection>();
        try {
            using var stream=new MemoryStream(data,false);
            if(extension==".pdf") {
                if(!data.AsSpan().StartsWith("%PDF-"u8))throw new FormatException();
                var raw=Encoding.Latin1.GetString(data);
                if(new[]{"/Encrypt","/JavaScript","/JS ","/Launch","/EmbeddedFile","/RichMedia","/XFA"}.Any(raw.Contains))throw new FormatException();
                using var pdf=PdfDocument.Open(stream,new ParsingOptions{UseLenientParsing=false});
                if(pdf.NumberOfPages>200)throw new BusinessException("knowledgeDocumentLimit");
                foreach(var page in pdf.GetPages()){ct.ThrowIfCancellationRequested();result.Add(new("Page "+page.Number,page.Text));CheckLength(result);}
            } else if(extension==".docx") {
                if(!data.AsSpan().StartsWith("PK"u8))throw new FormatException();
                using(var zip=new ZipArchive(stream,ZipArchiveMode.Read,true)) {
                    if(zip.Entries.Count>500||zip.Entries.Sum(e=>e.Length)>20_000_000||zip.GetEntry("word/document.xml") is null)throw new FormatException();
                    if(zip.Entries.Any(e=>e.FullName.Contains("vba",StringComparison.OrdinalIgnoreCase)||e.FullName.Contains("embeddings/",StringComparison.OrdinalIgnoreCase)))throw new FormatException();
                    foreach(var entry in zip.Entries.Where(e=>e.FullName.EndsWith(".rels",StringComparison.OrdinalIgnoreCase))) {
                        using var reader=new StreamReader(entry.Open());if(reader.ReadToEnd().Contains("TargetMode=\"External\"",StringComparison.OrdinalIgnoreCase))throw new FormatException();
                    }
                }
                stream.Position=0;
                using var word=WordprocessingDocument.Open(stream,false,new OpenSettings{MaxCharactersInPart=options.MaxCharacters*4L});
                if(word.MainDocumentPart?.Document?.Body is not Body body)throw new FormatException();
                var section="Document";var text=new StringBuilder();
                foreach(var paragraph in body.Descendants<Paragraph>()) {
                    ct.ThrowIfCancellationRequested();
                    if(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value?.StartsWith("Heading",StringComparison.OrdinalIgnoreCase)==true) {
                        if(text.Length>0){result.Add(new(section,text.ToString()));text.Clear();}section=paragraph.InnerText[..Math.Min(paragraph.InnerText.Length,150)];
                    }
                    text.AppendLine(paragraph.InnerText);if(text.Length+result.Sum(s=>s.Text.Length)>options.MaxCharacters)throw new BusinessException("knowledgeDocumentLimit");
                }
                if(text.Length>0)result.Add(new(section,text.ToString()));
            } else if(extension is ".txt" or ".md") {
                var text=new UTF8Encoding(false,true).GetString(data).TrimStart('\uFEFF');
                if(text.Any(c=>char.IsControl(c)&&c is not ('\n' or '\r' or '\t')))throw new FormatException();
                if(text.StartsWith("%PDF-")||data.AsSpan().StartsWith("PK"u8)||text.TrimStart().StartsWith("<html",StringComparison.OrdinalIgnoreCase))throw new FormatException();
                if(extension==".md") {
                    string heading="Document";var buffer=new StringBuilder();
                    foreach(var line in text.Split('\n')){ct.ThrowIfCancellationRequested();if(line.StartsWith('#')){if(buffer.Length>0){result.Add(new(heading,buffer.ToString()));buffer.Clear();}heading=line.TrimStart('#',' ')[..Math.Min(line.TrimStart('#',' ').Length,150)];}buffer.AppendLine(line);}
                    if(buffer.Length>0)result.Add(new(heading,buffer.ToString()));
                } else result.Add(new("Document",text));
            } else throw new BusinessException("knowledgeUnsupportedFormat");
            CheckLength(result);
            if(result.All(s=>string.IsNullOrWhiteSpace(s.Text)))throw new BusinessException(extension==".pdf"?"knowledgeOcrRequired":"knowledgeEmptyDocument");
            return result.Select(s=>s with{Text=Normalize(s.Text)}).ToList();
        }
        catch(BusinessException){throw;}catch(OperationCanceledException){throw;}
        catch(Exception ex)when(ex is not OutOfMemoryException){throw new BusinessException("knowledgeInvalidFile",400);}
    }
    private void CheckLength(List<ExtractedSection> sections){if(sections.Sum(s=>s.Text.Length)>options.MaxCharacters)throw new BusinessException("knowledgeDocumentLimit");}
    public static string Normalize(string text)=>Regex.Replace(text.Normalize(NormalizationForm.FormC).Replace("\r\n","\n"),@"[^\S\n]+"," ").Trim();
}
public sealed class KnowledgeChunker(KnowledgeOptions options):IKnowledgeChunker
{
    public List<TextChunk> Split(List<ExtractedSection> sections) {
        var result=new List<TextChunk>();
        foreach(var section in sections) {
            var start=0;while(start<section.Text.Length) {
                var end=Math.Min(start+options.ChunkCharacters,section.Text.Length);
                if(end<section.Text.Length){var boundary=section.Text.LastIndexOfAny(['\n','.','!','?'],end-1,end-start);if(boundary>start+options.ChunkCharacters/2)end=boundary+1;}
                var text=section.Text[start..end].Trim();if(text.Length>0)result.Add(new(result.Count,section.Reference,text));
                if(result.Count>options.MaxChunks)throw new BusinessException("knowledgeDocumentLimit");
                if(end==section.Text.Length)break;start=Math.Max(start+1,end-options.OverlapCharacters);
            }
        }
        return result;
    }
}
