import { Component, OnInit, signal } from '@angular/core';
import { NgIf, NgFor, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { AutomationRule } from '../../core/models/models';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-automation',
  standalone: true,
  imports: [NgIf, NgFor, FormsModule, DatePipe],
  template: `
    <div class="automation-page">
      <div class="page-header">
        <div class="header-content">
          <h1>Automation</h1>
          <p>Create rules that execute actions automatically</p>
        </div>
        <button class="btn btn-primary" (click)="showCreate.set(true)">
          <span class="material-icons">add</span> New Rule
        </button>
      </div>

      <div class="stats-row" *ngIf="rules().length > 0">
        <div class="mini-stat">
          <span class="material-icons">bolt</span>
          <div><span class="stat-num">{{ rules().length }}</span><span class="stat-label">Total Rules</span></div>
        </div>
        <div class="mini-stat">
          <span class="material-icons">check_circle</span>
          <div><span class="stat-num">{{ activeCount() }}</span><span class="stat-label">Active</span></div>
        </div>
        <div class="mini-stat">
          <span class="material-icons">play_arrow</span>
          <div><span class="stat-num">{{ totalRuns() }}</span><span class="stat-label">Total Runs</span></div>
        </div>
      </div>

      <div class="templates-section" *ngIf="templates().length > 0 && rules().length === 0">
        <div class="section-header">
          <span class="material-icons">auto_awesome</span>
          <h3>Quick Start Templates</h3>
        </div>
        <div class="templates-grid">
          <div *ngFor="let t of templates()" class="template-card" (click)="useTemplate(t)">
            <div class="template-icon">
              <span class="material-icons">bolt</span>
            </div>
            <h4>{{ t.name }}</h4>
            <p>{{ t.description }}</p>
            <span class="template-trigger">Trigger: {{ t.triggerType }}</span>
          </div>
        </div>
      </div>

      <div class="rules-section">
        <div *ngIf="loading()" class="loading-state"><div class="spinner"></div></div>
        <div *ngIf="!loading() && rules().length === 0" class="empty-state">
          <div class="empty-icon">
            <span class="material-icons">bolt</span>
          </div>
          <h3>No automation rules yet</h3>
          <p>Create your first rule or use a template above to get started</p>
          <button class="btn btn-primary" (click)="showCreate.set(true)">
            <span class="material-icons">add</span> Create Rule
          </button>
        </div>
        <div *ngFor="let rule of rules()" class="rule-card" [class.inactive]="!rule.isActive">
          <div class="rule-left">
            <div class="rule-icon" [class.active]="rule.isActive">
              <span class="material-icons">{{ rule.isActive ? 'bolt' : 'pause' }}</span>
            </div>
            <div class="rule-info">
              <h4>{{ rule.name }}</h4>
              <div class="rule-meta">
                <span class="trigger-badge">{{ rule.triggerType }}</span>
                <span class="runs-count">{{ rule.runCount }} runs</span>
                <span *ngIf="rule.lastRunAt" class="last-run">Last: {{ rule.lastRunAt | date:'short' }}</span>
              </div>
            </div>
          </div>
          <div class="rule-right">
            <label class="toggle">
              <input type="checkbox" [checked]="rule.isActive" (change)="toggleRule(rule)" />
              <span class="toggle-slider"></span>
            </label>
            <button class="icon-btn delete" (click)="deleteRule(rule.id)" title="Delete rule">
              <span class="material-icons">delete_outline</span>
            </button>
          </div>
        </div>
      </div>

      <div class="modal-overlay" *ngIf="showCreate()" (click)="showCreate.set(false)">
        <div class="modal" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h2>Create Automation Rule</h2>
            <button class="icon-btn" (click)="showCreate.set(false)">
              <span class="material-icons">close</span>
            </button>
          </div>
          <form (ngSubmit)="createRule()">
            <div class="form-group">
              <label>Rule Name *</label>
              <input type="text" [(ngModel)]="newRule.name" name="name" required placeholder="e.g., Email to Task" />
            </div>
            <div class="form-group">
              <label>Trigger Type *</label>
              <select [(ngModel)]="newRule.triggerType" name="triggerType" required class="responsive-select">
                <option value="email_received">Email Received</option>
                <option value="task_due_soon">Task Due Soon</option>
                <option value="task_overdue">Task Overdue</option>
                <option value="document_uploaded">Document Uploaded</option>
                <option value="event_created">Event Created</option>
                <option value="task_status_changed">Task Status Changed</option>
                <option value="schedule">Schedule (Cron)</option>
              </select>
            </div>
            <div class="form-group">
              <label>Notification Message</label>
              <textarea [(ngModel)]="newRule.message" name="message" rows="3" placeholder="What to notify? Use {variables} like {title}, {from}"></textarea>
            </div>
            <div class="modal-actions">
              <button type="button" class="btn btn-secondary" (click)="showCreate.set(false)">Cancel</button>
              <button type="submit" class="btn btn-primary" [disabled]="!newRule.name">
                <span class="material-icons">add</span> Create Rule
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .automation-page { padding: 2rem; }
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; }
    .header-content h1 { margin: 0; color: #e0e0e0; font-size: 1.8rem; }
    .header-content p { margin: 0.3rem 0 0; color: #888; }
    .stats-row { display: flex; gap: 1.5rem; margin-bottom: 2rem; }
    .mini-stat { display: flex; align-items: center; gap: 0.8rem; background: #1a1a2e; border: 1px solid #2a2a4a; border-radius: 12px; padding: 1rem 1.5rem; }
    .mini-stat .material-icons { font-size: 24px; color: #667eea; }
    .stat-num { display: block; color: #e0e0e0; font-size: 1.3rem; font-weight: 700; }
    .stat-label { color: #888; font-size: 0.8rem; }
    .section-header { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 1rem; }
    .section-header .material-icons { color: #667eea; }
    .section-header h3 { margin: 0; color: #e0e0e0; }
    .templates-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 1rem; margin-bottom: 2rem; }
    .template-card { background: #1a1a2e; border: 1px solid #2a2a4a; border-radius: 14px; padding: 1.3rem; cursor: pointer; transition: all 0.3s; }
    .template-card:hover { border-color: #667eea; transform: translateY(-3px); box-shadow: 0 8px 25px rgba(0,0,0,0.3); }
    .template-icon { width: 44px; height: 44px; border-radius: 10px; background: linear-gradient(135deg, #667eea, #764ba2); display: flex; align-items: center; justify-content: center; margin-bottom: 0.8rem; }
    .template-icon .material-icons { color: #fff; font-size: 22px; }
    .template-card h4 { margin: 0 0 0.3rem; color: #e0e0e0; font-size: 0.95rem; }
    .template-card p { margin: 0 0 0.5rem; color: #888; font-size: 0.8rem; line-height: 1.5; }
    .template-trigger { color: #667eea; font-size: 0.75rem; font-weight: 500; }
    .rule-card { display: flex; align-items: center; justify-content: space-between; background: #1a1a2e; border: 1px solid #2a2a4a; border-radius: 14px; padding: 1.2rem 1.5rem; margin-bottom: 0.8rem; transition: all 0.2s; }
    .rule-card:hover { border-color: #333; }
    .rule-card.inactive { opacity: 0.6; }
    .rule-left { display: flex; align-items: center; gap: 1rem; flex: 1; }
    .rule-icon { width: 42px; height: 42px; border-radius: 10px; background: #2a2a3e; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .rule-icon.active { background: rgba(102,126,234,0.15); }
    .rule-icon .material-icons { font-size: 20px; color: #888; }
    .rule-icon.active .material-icons { color: #667eea; }
    .rule-info h4 { margin: 0 0 0.3rem; color: #e0e0e0; font-size: 1rem; }
    .rule-meta { display: flex; align-items: center; gap: 0.8rem; flex-wrap: wrap; }
    .trigger-badge { background: rgba(102,126,234,0.1); color: #667eea; padding: 0.15rem 0.6rem; border-radius: 6px; font-size: 0.75rem; font-weight: 500; }
    .runs-count { color: #888; font-size: 0.8rem; }
    .last-run { color: #666; font-size: 0.8rem; }
    .rule-right { display: flex; align-items: center; gap: 0.8rem; }
    .toggle { position: relative; display: inline-block; width: 44px; height: 24px; }
    .toggle input { opacity: 0; width: 0; height: 0; }
    .toggle-slider { position: absolute; cursor: pointer; inset: 0; background: #333; border-radius: 24px; transition: 0.3s; }
    .toggle-slider:before { content: ""; position: absolute; height: 18px; width: 18px; left: 3px; bottom: 3px; background: #fff; border-radius: 50%; transition: 0.3s; }
    .toggle input:checked + .toggle-slider { background: #667eea; }
    .toggle input:checked + .toggle-slider:before { transform: translateX(20px); }
    .icon-btn { width: 36px; height: 36px; border: none; border-radius: 8px; background: transparent; color: #888; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.2s; }
    .icon-btn:hover { background: #2a2a3e; color: #e0e0e0; }
    .icon-btn.delete:hover { background: rgba(245,87,108,0.15); color: #f5576c; }
    .modal-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; }
    .modal-header h2 { margin: 0; color: #e0e0e0; font-size: 1.3rem; }
    .responsive-select { width: 100%; appearance: none; background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%23888' d='M6 8L1 3h10z'/%3E%3C/svg%3E"); background-repeat: no-repeat; background-position: right 12px center; padding-right: 36px !important; }
    .empty-state .empty-icon { width: 80px; height: 80px; border-radius: 50%; background: rgba(102,126,234,0.1); display: flex; align-items: center; justify-content: center; margin: 0 auto 1.5rem; }
    .empty-state .empty-icon .material-icons { font-size: 36px; color: #667eea; }
    .empty-state h3 { margin: 0 0 0.5rem; color: #e0e0e0; }
    .empty-state p { margin: 0 0 1.5rem; color: #888; }
    .btn { display: inline-flex; align-items: center; gap: 0.4rem; padding: 0.7rem 1.4rem; border: none; border-radius: 10px; font-size: 0.9rem; font-weight: 600; cursor: pointer; transition: all 0.2s; }
    .btn .material-icons { font-size: 18px; }
    .btn-primary { background: linear-gradient(135deg, #667eea, #764ba2); color: #fff; }
    .btn-primary:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 4px 15px rgba(102,126,234,0.4); }
    .btn-secondary { background: #2a2a3e; color: #e0e0e0; border: 1px solid #444; }
    .btn:disabled { opacity: 0.5; cursor: not-allowed; }
    @media (max-width: 768px) {
      .stats-row { flex-direction: column; }
      .templates-grid { grid-template-columns: 1fr; }
      .page-header { flex-direction: column; gap: 1rem; align-items: flex-start; }
    }
  `]
})
export class AutomationComponent implements OnInit {
  rules = signal<AutomationRule[]>([]);
  templates = signal<any[]>([]);
  loading = signal(true);
  showCreate = signal(false);
  newRule = { name: '', triggerType: 'email_received', message: '' };

  activeCount = signal(0);
  totalRuns = signal(0);

  constructor(private api: ApiService, private toast: ToastService) {}

  ngOnInit(): void {
    this.loadRules();
    this.api.getAutomationTemplates().subscribe(t => this.templates.set(t));
  }

  loadRules(): void {
    this.loading.set(true);
    this.api.getAutomationRules().subscribe(r => {
      this.rules.set(r);
      this.activeCount.set(r.filter(x => x.isActive).length);
      this.totalRuns.set(r.reduce((sum, x) => sum + (x.runCount || 0), 0));
      this.loading.set(false);
    });
  }

  useTemplate(t: any): void {
    this.newRule = { name: t.name, triggerType: t.triggerType, message: t.actions?.[0]?.config?.message || '' };
    this.showCreate.set(true);
  }

  createRule(): void {
    if (!this.newRule.name) return;
    const rule = {
      name: this.newRule.name,
      triggerType: this.newRule.triggerType,
      triggerConfig: '{}',
      actionsJson: JSON.stringify([{ type: 'send_notification', config: { message: this.newRule.message || 'Automation triggered' }, order: 0 }])
    };
    this.api.createAutomationRule(rule).subscribe({
      next: () => { this.showCreate.set(false); this.newRule = { name: '', triggerType: 'email_received', message: '' }; this.toast.success('Rule created'); this.loadRules(); },
      error: () => this.toast.error('Failed to create rule')
    });
  }

  toggleRule(rule: AutomationRule): void {
    this.api.updateAutomationRule(rule.id, { isActive: !rule.isActive }).subscribe({
      next: () => this.loadRules(),
      error: () => this.toast.error('Failed to update rule')
    });
  }

  deleteRule(id: number): void {
    if (!confirm('Delete this automation rule?')) return;
    this.api.deleteAutomationRule(id).subscribe({
      next: () => { this.toast.success('Rule deleted'); this.loadRules(); },
      error: () => this.toast.error('Failed to delete rule')
    });
  }
}
