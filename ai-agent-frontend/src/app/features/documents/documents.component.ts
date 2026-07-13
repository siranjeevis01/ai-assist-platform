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
    <div class="documents-page">
      <div class="page-header">
        <div class="header-content">
          <h1>Documents</h1>
          <p>Upload and query your documents with AI</p>
        </div>
        <label class="btn btn-primary upload-trigger">
          <span class="material-icons">upload_file</span> Upload Document
          <input type="file" (change)="onFileSelected($event)" accept=".txt,.csv,.json,.html,.xml,.md,.pdf,.doc,.docx" hidden />
        </label>
      </div>

      <div class="query-section" *ngIf="documents().length > 0">
        <div class="query-card">
          <div class="query-header">
            <span class="material-icons">psychology</span>
            <h3>Ask anything across all documents</h3>
          </div>
          <div class="query-input">
            <input type="text" [(ngModel)]="globalQuery" placeholder="e.g., What are the key findings in my reports?" (keyup.enter)="queryAll()" />
            <button class="btn btn-primary" (click)="queryAll()" [disabled]="querying() || !globalQuery.trim()">
              <span class="material-icons">{{ querying() ? 'hourglass_empty' : 'search' }}</span>
            </button>
          </div>
          <div class="query-result" *ngIf="globalAnswer()">
            <div class="result-header">
              <span class="material-icons">auto_awesome</span>
              <span>AI Answer</span>
            </div>
            <pre>{{ globalAnswer() }}</pre>
          </div>
        </div>
      </div>

      <div class="docs-list">
        <div *ngIf="loading()" class="loading-state"><div class="spinner"></div></div>
        <div *ngIf="!loading() && documents().length === 0" class="empty-state">
          <div class="empty-icon">
            <span class="material-icons">description</span>
          </div>
          <h3>No documents yet</h3>
          <p>Upload your first document to start asking AI-powered questions</p>
          <label class="btn btn-primary upload-trigger">
            <span class="material-icons">upload_file</span> Upload First Document
            <input type="file" (change)="onFileSelected($event)" accept=".txt,.csv,.json,.html,.xml,.md,.pdf,.doc,.docx" hidden />
          </label>
        </div>
        <div *ngFor="let doc of documents()" class="doc-card">
          <div class="doc-icon" [style.background]="getDocGradient(doc.contentType)">
            <span class="material-icons">{{ getDocIcon(doc.contentType) }}</span>
          </div>
          <div class="doc-info">
            <h4>{{ doc.fileName }}</h4>
            <div class="doc-meta">
              <span>{{ formatSize(doc.sizeBytes) }}</span>
              <span class="dot">&middot;</span>
              <span>{{ doc.createdAt | date:'mediumDate' }}</span>
              <span class="dot">&middot;</span>
              <span class="status-badge" [class]="doc.embeddingStatus">{{ doc.embeddingStatus }}</span>
            </div>
            <p *ngIf="doc.summary" class="doc-summary">{{ doc.summary }}</p>
          </div>
          <div class="doc-actions">
            <button class="icon-btn" (click)="openQuery(doc)" title="Ask about this document">
              <span class="material-icons">question_answer</span>
            </button>
            <button class="icon-btn delete" (click)="deleteDoc(doc.id)" title="Delete document">
              <span class="material-icons">delete_outline</span>
            </button>
          </div>
        </div>
      </div>

      <div class="modal-overlay" *ngIf="selectedDoc()" (click)="selectedDoc.set(null)">
        <div class="modal" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <div>
              <h2>{{ selectedDoc()!.fileName }}</h2>
              <span class="modal-subtitle">{{ formatSize(selectedDoc()!.sizeBytes) }} &middot; {{ selectedDoc()!.contentType }}</span>
            </div>
            <button class="icon-btn" (click)="selectedDoc.set(null)">
              <span class="material-icons">close</span>
            </button>
          </div>
          <div class="doc-query">
            <div class="query-input">
              <input type="text" [(ngModel)]="docQuery" placeholder="Ask a question about this document..." (keyup.enter)="queryDoc()" />
              <button class="btn btn-primary btn-sm" (click)="queryDoc()" [disabled]="querying() || !docQuery.trim()">
                <span class="material-icons">{{ querying() ? 'hourglass_empty' : 'send' }}</span>
              </button>
            </div>
            <div class="query-result" *ngIf="docAnswer()">
              <div class="result-header">
                <span class="material-icons">auto_awesome</span>
                <span>AI Answer</span>
              </div>
              <pre>{{ docAnswer() }}</pre>
            </div>
          </div>
          <div class="text-preview" *ngIf="selectedDoc()!.textPreview">
            <h4>Document Content</h4>
            <pre>{{ selectedDoc()!.textPreview }}</pre>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .documents-page { padding: 2rem; }
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; }
    .header-content h1 { margin: 0; color: var(--text-primary); font-size: 1.8rem; }
    .header-content p { margin: 0.3rem 0 0; color: var(--text-secondary); }
    .upload-trigger { cursor: pointer; }
    .query-section { margin-bottom: 2rem; }
    .query-card { background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 16px; padding: 1.5rem; }
    .query-header { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 1rem; }
    .query-header .material-icons { color: var(--primary-color); }
    .query-header h3 { margin: 0; color: var(--text-primary); font-size: 1rem; }
    .query-input { display: flex; gap: 0.5rem; }
    .query-input input { flex: 1; padding: 0.8rem 1rem; background: var(--bg-hover); border: 1px solid var(--border-color); border-radius: 10px; color: var(--text-primary); font-size: 0.95rem; transition: border-color 0.2s; }
    .query-input input:focus { border-color: var(--primary-color); outline: none; }
    .query-input input::placeholder { color: var(--text-secondary); }
    .query-result { margin-top: 1rem; background: var(--bg-hover); border-radius: 12px; padding: 1rem; }
    .result-header { display: flex; align-items: center; gap: 0.4rem; margin-bottom: 0.6rem; }
    .result-header .material-icons { font-size: 18px; color: var(--primary-color); }
    .result-header span { color: var(--text-secondary); font-size: 0.8rem; font-weight: 500; }
    .query-result pre { margin: 0; white-space: pre-wrap; color: var(--text-primary); font-size: 0.9rem; font-family: inherit; line-height: 1.7; }
    .doc-card { display: flex; align-items: center; gap: 1rem; background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 14px; padding: 1.2rem 1.5rem; margin-bottom: 0.8rem; transition: all 0.2s; }
    .doc-card:hover { border-color: var(--border-color); }
    .doc-icon { width: 48px; height: 48px; border-radius: 12px; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .doc-icon .material-icons { font-size: 24px; color: white; }
    .doc-info { flex: 1; min-width: 0; }
    .doc-info h4 { margin: 0 0 0.3rem; color: var(--text-primary); font-size: 0.95rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .doc-meta { display: flex; align-items: center; gap: 0.4rem; color: var(--text-secondary); font-size: 0.8rem; flex-wrap: wrap; }
    .dot { color: var(--text-muted); }
    .status-badge { padding: 0.1rem 0.5rem; border-radius: 6px; font-size: 0.7rem; font-weight: 500; text-transform: capitalize; }
    .status-badge.completed { background: rgba(67,233,123,0.1); color: #43e97b; }
    .status-badge.pending { background: rgba(255,167,38,0.1); color: #ffa726; }
    .doc-summary { margin: 0.4rem 0 0; color: var(--text-icon); font-size: 0.8rem; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; line-height: 1.5; }
    .doc-actions { display: flex; gap: 0.3rem; flex-shrink: 0; }
    .icon-btn { width: 36px; height: 36px; border: none; border-radius: 8px; background: transparent; color: var(--text-secondary); cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.2s; }
    .icon-btn:hover { background: var(--bg-hover); color: var(--text-primary); }
    .icon-btn.delete:hover { background: rgba(245,87,108,0.15); color: #f5576c; }
    .modal-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 1.5rem; }
    .modal-header h2 { margin: 0; color: var(--text-primary); font-size: 1.2rem; }
    .modal-subtitle { color: var(--text-secondary); font-size: 0.8rem; }
    .doc-query { margin-bottom: 1.5rem; }
    .text-preview { margin-top: 1rem; }
    .text-preview h4 { color: var(--text-secondary); font-size: 0.85rem; margin-bottom: 0.5rem; }
    .text-preview pre { background: var(--bg-hover); padding: 1rem; border-radius: 10px; max-height: 250px; overflow: auto; color: var(--text-primary); font-size: 0.85rem; font-family: inherit; white-space: pre-wrap; line-height: 1.6; }
    .btn-sm { padding: 0.5rem 0.8rem; }
    .empty-state .empty-icon { width: 80px; height: 80px; border-radius: 50%; background: rgba(102,126,234,0.1); display: flex; align-items: center; justify-content: center; margin: 0 auto 1.5rem; }
    .empty-state .empty-icon .material-icons { font-size: 36px; color: var(--primary-color); }
    .empty-state h3 { margin: 0 0 0.5rem; color: var(--text-primary); }
    .empty-state p { margin: 0 0 1.5rem; color: var(--text-secondary); }
    .btn { display: inline-flex; align-items: center; gap: 0.4rem; padding: 0.7rem 1.4rem; border: none; border-radius: 10px; font-size: 0.9rem; font-weight: 600; cursor: pointer; transition: all 0.2s; }
    .btn .material-icons { font-size: 18px; }
    .btn-primary { background: linear-gradient(135deg, #667eea, #764ba2); color: white; }
    .btn-primary:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 4px 15px rgba(102,126,234,0.4); }
    .btn:disabled { opacity: 0.5; cursor: not-allowed; }
    @media (max-width: 768px) {
      .page-header { flex-direction: column; gap: 1rem; align-items: flex-start; }
    }
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
    this.api.getDocuments().subscribe({ next: d => { this.documents.set(d); this.loading.set(false); }, error: () => this.loading.set(false) });
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
    this.api.queryAllDocuments(this.globalQuery).subscribe({ next: r => { this.globalAnswer = r.answer; this.querying.set(false); }, error: () => this.querying.set(false) });
  }

  openQuery(doc: DocumentInfo): void {
    this.selectedDoc.set(doc);
    this.docAnswer = '';
    this.docQuery = '';
  }

  queryDoc(): void {
    if (!this.docQuery.trim() || !this.selectedDoc()) return;
    this.querying.set(true);
    this.api.queryDocument(this.selectedDoc()!.id, this.docQuery).subscribe({ next: r => { this.docAnswer = r.answer; this.querying.set(false); }, error: () => this.querying.set(false) });
  }

  deleteDoc(id: number): void {
    if (!confirm('Delete this document permanently?')) return;
    this.api.deleteDocument(id).subscribe({ next: () => { this.toast.success('Document deleted'); this.loadDocs(); }, error: () => this.toast.error('Failed to delete') });
  }

  getDocIcon(type: string): string {
    if (type.includes('pdf')) return 'picture_as_pdf';
    if (type.includes('word')) return 'article';
    if (type.includes('json')) return 'data_object';
    if (type.includes('csv')) return 'table_chart';
    if (type.includes('html')) return 'code';
    return 'description';
  }

  getDocGradient(type: string): string {
    if (type.includes('pdf')) return 'linear-gradient(135deg, #f5576c, #ff7a5a)';
    if (type.includes('word')) return 'linear-gradient(135deg, #4facfe, #00f2fe)';
    if (type.includes('json')) return 'linear-gradient(135deg, #43e97b, #38f9d7)';
    return 'linear-gradient(135deg, #667eea, #764ba2)';
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }
}
