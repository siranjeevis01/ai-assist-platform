import { Component, OnInit, signal } from '@angular/core';
import { NgIf, NgFor } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { AutomationRule } from '../../core/models/models';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-automation',
  standalone: true,
  imports: [NgIf, NgFor, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header">
        <div class="header-content">
          <h1>Automation</h1>
          <p>Create rules that execute actions automatically</p>
        </div>
        <button class="btn btn-primary" (click)="showCreate.set(true)">
          <span class="material-icons">add</span> New Rule
        </button>
      </div>

      <div class="templates-section" *ngIf="templates().length > 0">
        <h3>Quick Start Templates</h3>
        <div class="templates-grid">
          <div *ngFor="let t of templates()" class="template-card" (click)="useTemplate(t)">
            <span class="material-icons template-icon">bolt</span>
            <h4>{{ t.name }}</h4>
            <p>{{ t.description }}</p>
          </div>
        </div>
      </div>

      <div class="rules-list">
        <div *ngIf="loading()" class="loading-state"><div class="spinner"></div></div>
        <div *ngIf="!loading() && rules().length === 0" class="empty-state">
          <span class="material-icons" style="font-size:48px;color:#667eea">bolt</span>
          <h3>No automation rules yet</h3>
          <p>Create your first rule or use a template above</p>
        </div>
        <div *ngFor="let rule of rules()" class="rule-card">
          <div class="rule-header">
            <div class="rule-info">
              <h4>{{ rule.name }}</h4>
              <span class="rule-trigger">Trigger: {{ rule.triggerType }}</span>
            </div>
            <div class="rule-toggle">
              <label class="toggle">
                <input type="checkbox" [checked]="rule.isActive" (change)="toggleRule(rule)" />
                <span class="toggle-slider"></span>
              </label>
            </div>
          </div>
          <div class="rule-stats">
            <span>Runs: {{ rule.runCount }}</span>
            <span *ngIf="rule.lastRunAt">Last: {{ rule.lastRunAt | date:'short' }}</span>
            <span>Created: {{ rule.createdAt | date:'mediumDate' }}</span>
          </div>
          <div class="rule-actions">
            <button class="icon-btn" (click)="deleteRule(rule.id)" title="Delete">
              <span class="material-icons">delete</span>
            </button>
          </div>
        </div>
      </div>

      <div class="modal-overlay" *ngIf="showCreate()" (click)="showCreate.set(false)">
        <div class="modal" (click)="$event.stopPropagation()">
          <h2>Create Automation Rule</h2>
          <form (ngSubmit)="createRule()">
            <div class="form-group">
              <label>Rule Name *</label>
              <input type="text" [(ngModel)]="newRule.name" name="name" required placeholder="e.g., Email → Task" />
            </div>
            <div class="form-group">
              <label>Trigger Type *</label>
              <select [(ngModel)]="newRule.triggerType" name="triggerType" required>
                <option value="email_received">Email Received</option>
                <option value="task_due_soon">Task Due Soon</option>
                <option value="document_uploaded">Document Uploaded</option>
                <option value="schedule">Schedule (Cron)</option>
              </select>
            </div>
            <div class="form-group">
              <label>Notification Message</label>
              <textarea [(ngModel)]="newRule.message" name="message" rows="2" placeholder="What to notify? Use {variables}"></textarea>
            </div>
            <div class="modal-actions">
              <button type="button" class="btn btn-secondary" (click)="showCreate.set(false)">Cancel</button>
              <button type="submit" class="btn btn-primary">Create Rule</button>
            </div>
          </form>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .templates-section { margin-bottom: 2rem; }
    .templates-section h3 { margin-bottom: 1rem; color: #e0e0e0; }
    .templates-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 1rem; }
    .template-card { background: #1e1e2e; border: 1px solid #333; border-radius: 12px; padding: 1.2rem; cursor: pointer; transition: all 0.2s; }
    .template-card:hover { border-color: #667eea; transform: translateY(-2px); }
    .template-icon { font-size: 32px; color: #667eea; margin-bottom: 0.5rem; }
    .template-card h4 { margin: 0.5rem 0 0.3rem; color: #e0e0e0; font-size: 0.95rem; }
    .template-card p { margin: 0; color: #999; font-size: 0.8rem; }
    .rule-card { background: #1e1e2e; border: 1px solid #333; border-radius: 12px; padding: 1.2rem; margin-bottom: 1rem; }
    .rule-header { display: flex; justify-content: space-between; align-items: center; }
    .rule-info h4 { margin: 0 0 0.3rem; color: #e0e0e0; }
    .rule-trigger { color: #667eea; font-size: 0.85rem; }
    .rule-stats { display: flex; gap: 1.5rem; margin: 0.8rem 0; color: #888; font-size: 0.85rem; }
    .rule-actions { display: flex; gap: 0.5rem; justify-content: flex-end; }
    .toggle { position: relative; display: inline-block; width: 44px; height: 24px; }
    .toggle input { opacity: 0; width: 0; height: 0; }
    .toggle-slider { position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0; background: #444; border-radius: 24px; transition: 0.3s; }
    .toggle-slider:before { content: ""; position: absolute; height: 18px; width: 18px; left: 3px; bottom: 3px; background: white; border-radius: 50%; transition: 0.3s; }
    .toggle input:checked + .toggle-slider { background: #667eea; }
    .toggle input:checked + .toggle-slider:before { transform: translateX(20px); }
  `]
})
export class AutomationComponent implements OnInit {
  rules = signal<AutomationRule[]>([]);
  templates = signal<any[]>([]);
  loading = signal(true);
  showCreate = signal(false);
  newRule = { name: '', triggerType: 'email_received', message: '' };

  constructor(private api: ApiService, private toast: ToastService) {}

  ngOnInit(): void {
    this.loadRules();
    this.api.getAutomationTemplates().subscribe(t => this.templates.set(t));
  }

  loadRules(): void {
    this.loading.set(true);
    this.api.getAutomationRules().subscribe(r => { this.rules.set(r); this.loading.set(false); });
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
    this.api.createAutomationRule(rule).subscribe(() => {
      this.showCreate.set(false);
      this.toast.success('Rule created');
      this.loadRules();
    });
  }

  toggleRule(rule: AutomationRule): void {
    this.api.updateAutomationRule(rule.id, { isActive: !rule.isActive }).subscribe(() => this.loadRules());
  }

  deleteRule(id: number): void {
    if (!confirm('Delete this rule?')) return;
    this.api.deleteAutomationRule(id).subscribe(() => { this.toast.success('Rule deleted'); this.loadRules(); });
  }
}
