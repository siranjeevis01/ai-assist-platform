import { Component, OnInit, signal } from '@angular/core';
import { NgIf, NgFor, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { DocumentInfo } from '../../core/models/models';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-documents',
  standalone: true,
  imports: [NgIf, NgFor, DatePipe, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header">
        <div class="header-content">
          <h1>Documents</h1>
          <p>Upload and query your documents with AI</p>
        </div>
        <label class="btn btn-primary upload-btn">
          <span class="material-icons">upload_file</span> Upload
          <input type="file" (change)="onFileSelected($event)" accept=".txt,.csv,.json,.html,.xml,.md,.pdf,.doc,.docx" hidden />
        </label>
      </div>

      <div class="query-box" *ngIf="documents().length > 0">
        <h3>Ask a question across all documents</h3>
        <div class="query-input">
          <input type="text" [(ngModel)]="globalQuery" placeholder="Ask anything about your documents..." (keyup.enter)="queryAll()" />
          <button class="btn btn-primary" (click)="queryAll()" [disabled]="querying()">
            <span class="material-icons">{{ querying() ? 'hourglass_empty' : 'search' }}</span>
          </button>
        </div>
        <div class="query-result" *ngIf="globalAnswer()">
          <pre>{{ globalAnswer() }}</pre>
        </div>
      </div>

      <div class="docs-list">
        <div *ngIf="loading()" class="loading-state"><div class="spinner"></div></div>
        <div *ngIf="!loading() && documents().length === 0" class="empty-state">
          <span class="material-icons" style="font-size:48px;color:#667eea">description</span>
          <h3>No documents yet</h3>
          <p>Upload your first document to get started</p>
        </div>
        <div *ngFor="let doc of documents()" class="doc-card">
          <div class="doc-icon">
            <span class="material-icons">{{ getDocIcon(doc.contentType) }}</span>
          </div>
          <div class="doc-info">
            <h4>{{ doc.fileName }}</h4>
            <p class="doc-meta">{{ formatSize(doc.sizeBytes) }} &middot; {{ doc.createdAt | date:'mediumDate' }}</p>
            <p *ngIf="doc.summary" class="doc-summary">{{ doc.summary }}</p>
          </div>
          <div class="doc-actions">
            <button class="icon-btn" (click)="openQuery(doc)" title="Ask about this document">
              <span class="material-icons">question_answer</span>
            </button>
            <button class="icon-btn" (click)="deleteDoc(doc.id)" title="Delete">
              <span class="material-icons">delete</span>
            </button>
          </div>
        </div>
      </div>

      <div class="modal-overlay" *ngIf="selectedDoc()" (click)="selectedDoc.set(null)">
        <div class="modal" (click)="$event.stopPropagation()">
          <h2>Ask about: {{ selectedDoc()!.fileName }}</h2>
          <div class="query-input">
            <input type="text" [(ngModel)]="docQuery" placeholder="Type your question..." (keyup.enter)="queryDoc()" />
            <button class="btn btn-primary" (click)="queryDoc()" [disabled]="querying()">
              <span class="material-icons">{{ querying() ? 'hourglass_empty' : 'send' }}</span>
            </button>
          </div>
          <div class="query-result" *ngIf="docAnswer()">
            <pre>{{ docAnswer() }}</pre>
          </div>
          <div class="text-preview" *ngIf="selectedDoc()!.textPreview">
            <h4>Document Preview</h4>
            <pre>{{ selectedDoc()!.textPreview }}</pre>
          </div>
          <div class="modal-actions">
            <button class="btn btn-secondary" (click)="selectedDoc.set(null)">Close</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .upload-btn { cursor: pointer; }
    .query-box { background: #1e1e2e; border: 1px solid #333; border-radius: 12px; padding: 1.5rem; margin-bottom: 2rem; }
    .query-box h3 { margin: 0 0 1rem; color: #e0e0e0; font-size: 1rem; }
    .query-input { display: flex; gap: 0.5rem; }
    .query-input input { flex: 1; padding: 0.7rem 1rem; background: #2a2a3e; border: 1px solid #444; border-radius: 8px; color: #e0e0e0; font-size: 0.95rem; }
    .query-result { margin-top: 1rem; background: #2a2a3e; border-radius: 8px; padding: 1rem; }
    .query-result pre { margin: 0; white-space: pre-wrap; color: #ccc; font-size: 0.9rem; font-family: inherit; }
    .doc-card { display: flex; align-items: center; gap: 1rem; background: #1e1e2e; border: 1px solid #333; border-radius: 12px; padding: 1rem 1.2rem; margin-bottom: 0.8rem; }
    .doc-icon .material-icons { font-size: 36px; color: #667eea; }
    .doc-info { flex: 1; }
    .doc-info h4 { margin: 0 0 0.2rem; color: #e0e0e0; }
    .doc-meta { margin: 0; color: #888; font-size: 0.8rem; }
    .doc-summary { margin: 0.3rem 0 0; color: #aaa; font-size: 0.85rem; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
    .doc-actions { display: flex; gap: 0.3rem; }
    .text-preview { margin-top: 1rem; }
    .text-preview h4 { color: #888; font-size: 0.85rem; margin-bottom: 0.5rem; }
    .text-preview pre { background: #2a2a3e; padding: 1rem; border-radius: 8px; max-height: 200px; overflow: auto; color: #ccc; font-size: 0.85rem; font-family: inherit; white-space: pre-wrap; }
  `]
})
export class DocumentsComponent implements OnInit {
  documents = signal<DocumentInfo[]>([]);
  loading = signal(true);
  querying = signal(false);
  selectedDoc = signal<DocumentInfo | null>(null);
  globalQuery = '';
  globalAnswer = '';
  docQuery = '';
  docAnswer = '';

  constructor(private api: ApiService, private toast: ToastService) {}

  ngOnInit(): void { this.loadDocs(); }

  loadDocs(): void {
    this.loading.set(true);
    this.api.getDocuments().subscribe(d => { this.documents.set(d); this.loading.set(false); });
  }

  onFileSelected(event: any): void {
    const file = event.target.files?.[0];
    if (!file) return;
    this.api.uploadDocument(file).subscribe({
      next: () => { this.toast.success('Document uploaded'); this.loadDocs(); },
      error: (e) => this.toast.error(e.error?.error || 'Upload failed')
    });
    event.target.value = '';
  }

  queryAll(): void {
    if (!this.globalQuery.trim()) return;
    this.querying.set(true);
    this.api.queryAllDocuments(this.globalQuery).subscribe(r => { this.globalAnswer = r.answer; this.querying.set(false); });
  }

  openQuery(doc: DocumentInfo): void {
    this.selectedDoc.set(doc);
    this.docAnswer = '';
  }

  queryDoc(): void {
    if (!this.docQuery.trim() || !this.selectedDoc()) return;
    this.querying.set(true);
    this.api.queryDocument(this.selectedDoc()!.id, this.docQuery).subscribe(r => { this.docAnswer = r.answer; this.querying.set(false); });
  }

  deleteDoc(id: number): void {
    if (!confirm('Delete this document?')) return;
    this.api.deleteDocument(id).subscribe(() => { this.toast.success('Document deleted'); this.loadDocs(); });
  }

  getDocIcon(type: string): string {
    if (type.includes('pdf')) return 'picture_as_pdf';
    if (type.includes('word')) return 'article';
    if (type.includes('json')) return 'data_object';
    if (type.includes('csv')) return 'table_chart';
    return 'description';
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }
}
