import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/* "About the Research Assistant" — explains the RAG architecture behind the
   assistant: local embeddings + vector search, with larger models only as an
   explicit opt-in step. */

@Component({
  selector: 'app-assistant-about',
  imports: [RouterLink],
  templateUrl: './assistant-about.html',
})
export class AssistantAboutPage {
  readonly stages = [
    {
      num: '01',
      title: 'Import & Analysis',
      paras: [
        'Everything starts with the bill or policy document itself. When a document first enters the system, we add it to our database by hand, cross-referencing it against related records — its origin, authors, publishers, and legislative history. We write a summary of the document and assign tags that group similar bills and policies together, so related measures can be found side by side.',
      ],
    },
    {
      num: '02',
      title: 'Breaking Documents into Manageable Chunks',
      paras: [
        'Legislation is often long and difficult to read. To make its contents findable, we split the full text of every bill and policy into a few passages per page using straightforward PDF and text tooling. Each passage is stored in a table attached to the parent document, so every excerpt can always be traced back to its exact page and paragraph.',
      ],
    },
    {
      num: '03',
      title: 'Vector Embeddings & Search',
      paras: [
        'Each passage is then converted into a vector — a series of numbers that mathematically represents its meaning. This embedding step is one of the most lightweight functions a language model can perform: it runs on a small, locally hosted model efficient enough to operate on an ordinary laptop. It is not a chatbot in a massive data center.',
        'When you type a question into the search box, your text is converted into the same kind of vector, and the database finds the passages whose vectors sit closest to it. Imagine sorting a column in a spreadsheet where each row is a paragraph of a document — ordered not alphabetically, but by mathematical similarity of meaning. With almost no computing power, the search returns the passages most related to what you asked, down to the page and paragraph.',
      ],
    },
    {
      num: '04',
      title: 'Search Results',
      paras: [
        'The matched passages are combined with the summary we wrote at import, and the same small local model condenses them into a short synthesis, linking each source document to its relevant text. The result is an extremely efficient way to surface the documents — and the exact provisions — related to your question, with every claim traceable to its source.',
      ],
    },
    {
      num: '05',
      title: 'Follow-up Questions',
      paras: [
        'Everything described so far is a low-power search tool that relies on no commercial AI service. Follow-up questions and detailed cross-document comparison, however, are where small local models reach their limits. We believe new technology should be usable for learning and for work that aims at a positive impact — so larger models are offered only as an explicit additional step. The choice of whether to invoke them is always yours.',
      ],
    },
  ];
}
