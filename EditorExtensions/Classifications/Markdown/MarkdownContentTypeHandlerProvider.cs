﻿using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Html.Editor;
using Microsoft.Html.Editor.ContainedLanguage;
using System.Reflection;
using Microsoft.Html.Core;
using Microsoft.Web.Core;

namespace MadsKristensen.EditorExtensions.Classifications.Markdown
{
    [Export(typeof(IContentTypeHandlerProvider))]
    [ContentType(MarkdownContentTypeDefinition.MarkdownContentType)]
    public class MarkdownContentTypeHandlerProvider : IContentTypeHandlerProvider
    {
        [Import]
        public IContentTypeRegistryService ContentTypeRegistry { get; set; }

        public IContentTypeHandler GetContentTypeHandler()
        {
            return new MarkdownContentTypeHandler(ContentTypeRegistry);
        }
    }

    public class MarkdownContentTypeHandler : HtmlContentTypeHandler
    {
        static readonly Func<HtmlContentTypeHandler, List<LanguageBlockHandler>> GetLanguageBlockHandlerList =
            (Func<HtmlContentTypeHandler, List<LanguageBlockHandler>>)
            Delegate.CreateDelegate(
                typeof(Func<HtmlContentTypeHandler, List<LanguageBlockHandler>>),
                typeof(HtmlContentTypeHandler).GetProperty("LanguageBlockHandlers", BindingFlags.NonPublic | BindingFlags.Instance).GetMethod
            );

        readonly IContentTypeRegistryService contentTypeRegistry;
        public MarkdownContentTypeHandler(IContentTypeRegistryService contentTypeRegistry)
        {
            this.contentTypeRegistry = contentTypeRegistry;
        }

        protected override void CreateBlockHandlers()
        {
            base.CreateBlockHandlers();
            GetLanguageBlockHandlerList(this).Add(new CodeBlockBlockHandler(EditorTree, contentTypeRegistry));
        }

        public override void Init(HtmlEditorTree editorTree)
        {
            base.Init(editorTree);
            ContainedLanguageSettings.FormatOnPaste = false;
            ContainedLanguageSettings.EnableSyntaxCheck = false;
        }

        public override IContentType GetContentTypeOfLocation(int position)
        {
            int itemContaining = EditorTree.ArtifactCollection.GetItemContaining(position);
            if (itemContaining >= 0)
            {
                IArtifact artifact = EditorTree.ArtifactCollection[itemContaining];
                if (artifact.TreatAs == ArtifactTreatAs.Comment)
                {
                    return contentTypeRegistry.GetContentType("text");
                }
            }
            return base.GetContentTypeOfLocation(position);
        }
        public override ArtifactCollection CreateArtifactCollection()
        {
            return new ArtifactCollection(new MarkdownCodeArtifactProcessor());
        }
    }

    class CodeBlockBlockHandler : ArtifactBasedBlockHandler
    {
        readonly IContentTypeRegistryService contentTypeRegistry;
        public CodeBlockBlockHandler(HtmlEditorTree tree, IContentTypeRegistryService contentTypeRegistry)
            : base(tree, null)
        {
            this.contentTypeRegistry = contentTypeRegistry;
        }
        protected override BufferGenerator CreateBufferGenerator()
        {
            return new MarkdownBufferGenerator(EditorTree, LanguageBlocks);
        }
        public override IContentType GetContentTypeOfLocation(int position)
        {
            LanguageBlock block = this.GetLanguageBlockOfLocation(position);
            if (block == null) return null;
            var alb = block as ArtifactLanguageBlock;
            if (alb != null)
                return contentTypeRegistry.GetContentType(alb.Language);
            else
                return contentTypeRegistry.GetContentType("text");
        }
    }

    class ArtifactLanguageBlock : LanguageBlock
    {
        public ArtifactLanguageBlock(Artifact a) : base(a) { Language = a.ClassificationType; }
        public string Language { get; private set; }
    }

    class MarkdownBufferGenerator : ArtifactBasedBufferGenerator
    {
        public MarkdownBufferGenerator(HtmlEditorTree editorTree, LanguageBlockCollection languageBlocks) : base(editorTree, languageBlocks) { }
    }


    public class MarkdownCodeArtifactProcessor : IArtifactProcessor
    {
        public ArtifactCollection CreateArtifactCollection()
        {
            return new ArtifactCollection(this);
        }

        public void GetArtifacts(ITextProvider text, ArtifactCollection artifactCollection)
        {
            var parser = new MarkdownParser(new CharacterStream(text));
            parser.ArtifactFound += (s, e) => artifactCollection.Add(e.Artifact);
            parser.Parse();
        }

        public bool IsReady { get { return true; } }

        public string LeftSeparator { get { return "`"; } }
        public string RightSeparator { get { return "`"; } }
        public string LeftCommentSeparator { get { return "<!--"; } }
        public string RightCommentSeparator { get { return "<!--"; } }
    }
}