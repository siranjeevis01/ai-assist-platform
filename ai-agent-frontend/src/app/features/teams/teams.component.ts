import { Component, OnInit, signal } from '@angular/core';
import { NgIf, NgFor, NgClass, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { Team, TeamMember } from '../../core/models/models';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-teams',
  standalone: true,
  imports: [NgIf, NgFor, NgClass, DatePipe, FormsModule],
  template: `
    <div class="teams-page">
      <div class="page-header">
        <div class="header-content">
          <h1>Teams</h1>
          <p>Manage team collaboration and permissions</p>
        </div>
        <button class="btn btn-primary" (click)="showCreate.set(true)">
          <span class="material-icons">group_add</span> New Team
        </button>
      </div>

      <div class="teams-grid">
        <div *ngIf="loading()" class="loading-state"><div class="spinner"></div></div>
        <div *ngIf="!loading() && teams().length === 0" class="empty-state">
          <div class="empty-icon">
            <span class="material-icons">groups</span>
          </div>
          <h3>No teams yet</h3>
          <p>Create your first team to start collaborating with others</p>
          <button class="btn btn-primary" (click)="showCreate.set(true)">
            <span class="material-icons">group_add</span> Create Team
          </button>
        </div>
        <div *ngFor="let team of teams()" class="team-card" (click)="selectTeam(team)">
          <div class="team-avatar">
            <span>{{ team.name.charAt(0).toUpperCase() }}</span>
          </div>
          <div class="team-info">
            <h4>{{ team.name }}</h4>
            <p *ngIf="team.description">{{ team.description }}</p>
            <div class="team-meta">
              <span class="material-icons">event</span>
              <span>{{ team.createdAt | date:'mediumDate' }}</span>
            </div>
          </div>
          <span class="material-icons team-arrow">chevron_right</span>
        </div>
      </div>

      <div class="modal-overlay" *ngIf="showCreate()" (click)="showCreate.set(false)">
        <div class="modal" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h2>Create Team</h2>
            <button class="icon-btn" (click)="showCreate.set(false)">
              <span class="material-icons">close</span>
            </button>
          </div>
          <form (ngSubmit)="createTeam()">
            <div class="form-group">
              <label>Team Name *</label>
              <input type="text" [(ngModel)]="newTeamName" name="name" required placeholder="e.g., Engineering Team" />
            </div>
            <div class="form-group">
              <label>Description</label>
              <textarea [(ngModel)]="newTeamDesc" name="desc" rows="3" placeholder="What is this team for?"></textarea>
            </div>
            <div class="modal-actions">
              <button type="button" class="btn btn-secondary" (click)="showCreate.set(false)">Cancel</button>
              <button type="submit" class="btn btn-primary" [disabled]="!newTeamName.trim()">
                <span class="material-icons">group_add</span> Create Team
              </button>
            </div>
          </form>
        </div>
      </div>

      <div class="modal-overlay" *ngIf="selectedTeam()" (click)="selectedTeam.set(null)">
        <div class="modal modal-lg" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <div>
              <h2>{{ selectedTeam()!.name }}</h2>
              <span *ngIf="selectedTeam()!.description" class="modal-subtitle">{{ selectedTeam()!.description }}</span>
            </div>
            <button class="icon-btn" (click)="selectedTeam.set(null)">
              <span class="material-icons">close</span>
            </button>
          </div>

          <div class="members-section">
            <div class="members-header">
              <h3>Members ({{ members().length }})</h3>
              <button class="btn btn-sm btn-primary" (click)="showAddMember.set(!showAddMember())">
                <span class="material-icons">{{ showAddMember() ? 'close' : 'person_add' }}</span>
                {{ showAddMember() ? 'Cancel' : 'Add Member' }}
              </button>
            </div>

            <div class="add-member-form" *ngIf="showAddMember()">
              <input type="email" [(ngModel)]="addMemberEmail" placeholder="Email address" />
              <select [(ngModel)]="addMemberRole" class="role-select">
                <option value="Member">Member</option>
                <option value="Admin">Admin</option>
                <option value="Viewer">Viewer</option>
              </select>
              <button class="btn btn-primary btn-sm" (click)="addMember()" [disabled]="!addMemberEmail.trim()">
                <span class="material-icons">person_add</span> Add
              </button>
            </div>

            <div *ngIf="members().length === 0" class="empty-members">
              <span class="material-icons">person_off</span>
              <p>No members yet</p>
            </div>

            <div *ngFor="let m of members()" class="member-row">
              <div class="member-avatar" [ngClass]="m.role.toLowerCase()">{{ (m.user?.name || 'U').charAt(0) }}</div>
              <div class="member-info">
                <span class="member-name">{{ m.user?.name || 'User #' + m.userId }}</span>
                <span class="member-email">{{ m.user?.email || '' }}</span>
              </div>
              <span class="role-badge" [ngClass]="m.role.toLowerCase()">{{ m.role }}</span>
              <button *ngIf="m.role !== 'Owner'" class="icon-btn delete" (click)="removeMember(m.userId)" title="Remove member">
                <span class="material-icons">person_remove</span>
              </button>
            </div>
          </div>

          <div class="modal-footer">
            <button class="btn btn-danger" (click)="deleteTeam()">
              <span class="material-icons">delete</span> Delete Team
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .teams-page { padding: 2rem; }
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; }
    .header-content h1 { margin: 0; color: #e0e0e0; font-size: 1.8rem; }
    .header-content p { margin: 0.3rem 0 0; color: #888; }
    .teams-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); gap: 1rem; }
    .team-card { display: flex; align-items: center; gap: 1rem; background: #1a1a2e; border: 1px solid #2a2a4a; border-radius: 14px; padding: 1.2rem 1.5rem; cursor: pointer; transition: all 0.3s; }
    .team-card:hover { border-color: #667eea; transform: translateY(-2px); box-shadow: 0 8px 25px rgba(0,0,0,0.3); }
    .team-avatar { width: 50px; height: 50px; border-radius: 14px; background: linear-gradient(135deg, #667eea, #764ba2); display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .team-avatar span { color: #fff; font-size: 1.2rem; font-weight: 700; }
    .team-info { flex: 1; min-width: 0; }
    .team-info h4 { margin: 0 0 0.2rem; color: #e0e0e0; font-size: 1.05rem; }
    .team-info p { margin: 0 0 0.3rem; color: #888; font-size: 0.8rem; display: -webkit-box; -webkit-line-clamp: 1; -webkit-box-orient: vertical; overflow: hidden; }
    .team-meta { display: flex; align-items: center; gap: 0.3rem; color: #666; font-size: 0.75rem; }
    .team-meta .material-icons { font-size: 14px; }
    .team-arrow { color: #444; }
    .modal-lg { max-width: 600px; }
    .modal-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 1.5rem; }
    .modal-header h2 { margin: 0; color: #e0e0e0; font-size: 1.3rem; }
    .modal-subtitle { color: #888; font-size: 0.85rem; }
    .members-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
    .members-header h3 { margin: 0; color: #e0e0e0; font-size: 1rem; }
    .add-member-form { display: flex; gap: 0.5rem; margin-bottom: 1rem; padding: 1rem; background: #2a2a3e; border-radius: 10px; }
    .add-member-form input { flex: 1; padding: 0.6rem 1rem; background: #1a1a2e; border: 1px solid #444; border-radius: 8px; color: #e0e0e0; font-size: 0.9rem; }
    .role-select { padding: 0.6rem; background: #1a1a2e; border: 1px solid #444; border-radius: 8px; color: #e0e0e0; appearance: none; background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%23888' d='M6 8L1 3h10z'/%3E%3C/svg%3E"); background-repeat: no-repeat; background-position: right 8px center; padding-right: 28px; }
    .empty-members { text-align: center; padding: 2rem; color: #666; }
    .empty-members .material-icons { font-size: 36px; margin-bottom: 0.5rem; }
    .empty-members p { margin: 0; font-size: 0.9rem; }
    .member-row { display: flex; align-items: center; gap: 0.8rem; padding: 0.8rem; border-radius: 10px; transition: background 0.2s; }
    .member-row:hover { background: #2a2a3e; }
    .member-avatar { width: 38px; height: 38px; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-weight: 600; color: #fff; font-size: 0.85rem; flex-shrink: 0; }
    .member-avatar.owner { background: linear-gradient(135deg, #667eea, #764ba2); }
    .member-avatar.admin { background: linear-gradient(135deg, #ffa726, #ff7043); }
    .member-avatar.member { background: linear-gradient(135deg, #43e97b, #38f9d7); }
    .member-avatar.viewer { background: linear-gradient(135deg, #78909c, #90a4ae); }
    .member-info { flex: 1; }
    .member-name { display: block; color: #e0e0e0; font-size: 0.9rem; }
    .member-email { color: #888; font-size: 0.8rem; }
    .role-badge { padding: 0.2rem 0.7rem; border-radius: 8px; font-size: 0.7rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; }
    .role-badge.owner { background: rgba(102,126,234,0.15); color: #667eea; }
    .role-badge.admin { background: rgba(255,167,38,0.15); color: #ffa726; }
    .role-badge.member { background: rgba(67,233,123,0.15); color: #43e97b; }
    .role-badge.viewer { background: rgba(120,144,156,0.15); color: #90a4ae; }
    .modal-footer { padding-top: 1rem; border-top: 1px solid #2a2a4a; margin-top: 1rem; }
    .btn-danger { background: rgba(245,87,108,0.15); color: #f5576c; border: 1px solid rgba(245,87,108,0.3); padding: 0.6rem 1.2rem; border-radius: 8px; cursor: pointer; display: inline-flex; align-items: center; gap: 0.4rem; font-size: 0.85rem; font-weight: 600; transition: all 0.2s; }
    .btn-danger:hover { background: rgba(245,87,108,0.25); }
    .icon-btn { width: 36px; height: 36px; border: none; border-radius: 8px; background: transparent; color: #888; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.2s; }
    .icon-btn:hover { background: #2a2a3e; color: #e0e0e0; }
    .icon-btn.delete:hover { background: rgba(245,87,108,0.15); color: #f5576c; }
    .btn { display: inline-flex; align-items: center; gap: 0.4rem; padding: 0.7rem 1.4rem; border: none; border-radius: 10px; font-size: 0.9rem; font-weight: 600; cursor: pointer; transition: all 0.2s; }
    .btn .material-icons { font-size: 18px; }
    .btn-primary { background: linear-gradient(135deg, #667eea, #764ba2); color: #fff; }
    .btn-primary:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 4px 15px rgba(102,126,234,0.4); }
    .btn-secondary { background: #2a2a3e; color: #e0e0e0; border: 1px solid #444; }
    .btn-sm { padding: 0.4rem 0.8rem; font-size: 0.85rem; }
    .btn:disabled { opacity: 0.5; cursor: not-allowed; }
    .empty-state .empty-icon { width: 80px; height: 80px; border-radius: 50%; background: rgba(102,126,234,0.1); display: flex; align-items: center; justify-content: center; margin: 0 auto 1.5rem; }
    .empty-state .empty-icon .material-icons { font-size: 36px; color: #667eea; }
    .empty-state h3 { margin: 0 0 0.5rem; color: #e0e0e0; }
    .empty-state p { margin: 0 0 1.5rem; color: #888; }
    @media (max-width: 768px) {
      .teams-grid { grid-template-columns: 1fr; }
      .page-header { flex-direction: column; gap: 1rem; align-items: flex-start; }
    }
  `]
})
export class TeamsComponent implements OnInit {
  teams = signal<Team[]>([]);
  members = signal<TeamMember[]>([]);
  loading = signal(true);
  showCreate = signal(false);
  showAddMember = signal(false);
  selectedTeam = signal<Team | null>(null);
  newTeamName = '';
  newTeamDesc = '';
  addMemberEmail = '';
  addMemberRole = 'Member';

  constructor(private api: ApiService, private toast: ToastService) {}

  ngOnInit(): void { this.loadTeams(); }

  loadTeams(): void {
    this.loading.set(true);
    this.api.getTeams().subscribe({ next: t => { this.teams.set(t); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  createTeam(): void {
    if (!this.newTeamName.trim()) return;
    this.api.createTeam({ name: this.newTeamName, description: this.newTeamDesc }).subscribe({
      next: () => { this.showCreate.set(false); this.newTeamName = ''; this.newTeamDesc = ''; this.toast.success('Team created'); this.loadTeams(); },
      error: () => this.toast.error('Failed to create team')
    });
  }

  selectTeam(team: Team): void {
    this.selectedTeam.set(team);
    this.api.getTeam(team.id).subscribe({
      next: t => { this.selectedTeam.set(t); this.members.set(t.members || []); },
      error: () => this.toast.error('Failed to load team details')
    });
  }

  addMember(): void {
    if (!this.addMemberEmail.trim() || !this.selectedTeam()) return;
    this.api.addTeamMember(this.selectedTeam()!.id, this.addMemberEmail, this.addMemberRole).subscribe({
      next: () => { this.toast.success('Member added'); this.addMemberEmail = ''; this.showAddMember.set(false); this.selectTeam(this.selectedTeam()!); },
      error: (e) => this.toast.error(e.error?.error || 'Failed to add member')
    });
  }

  removeMember(userId: number): void {
    if (!this.selectedTeam() || !confirm('Remove this member from the team?')) return;
    this.api.removeTeamMember(this.selectedTeam()!.id, userId).subscribe({
      next: () => { this.toast.success('Member removed'); this.selectTeam(this.selectedTeam()!); },
      error: (e) => this.toast.error(e.error?.error || 'Failed to remove member')
    });
  }

  deleteTeam(): void {
    if (!this.selectedTeam() || !confirm('Are you sure you want to delete this team permanently? This cannot be undone.')) return;
    this.api.deleteTeam(this.selectedTeam()!.id).subscribe({
      next: () => { this.toast.success('Team deleted'); this.selectedTeam.set(null); this.loadTeams(); },
      error: (e) => this.toast.error(e.error?.error || 'Failed to delete team')
    });
  }
}
