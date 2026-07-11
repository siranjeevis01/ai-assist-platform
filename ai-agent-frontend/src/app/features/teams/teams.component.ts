import { Component, OnInit, signal } from '@angular/core';
import { NgIf, NgFor, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { Team, TeamMember } from '../../core/models/models';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-teams',
  standalone: true,
  imports: [NgIf, NgFor, DatePipe, FormsModule],
  template: `
    <div class="page-container">
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
          <span class="material-icons" style="font-size:48px;color:#667eea">groups</span>
          <h3>No teams yet</h3>
          <p>Create your first team to start collaborating</p>
        </div>
        <div *ngFor="let team of teams()" class="team-card" (click)="selectTeam(team)">
          <div class="team-icon">
            <span class="material-icons">group</span>
          </div>
          <div class="team-info">
            <h4>{{ team.name }}</h4>
            <p *ngIf="team.description">{{ team.description }}</p>
            <span class="team-date">Created {{ team.createdAt | date:'mediumDate' }}</span>
          </div>
        </div>
      </div>

      <div class="modal-overlay" *ngIf="showCreate()" (click)="showCreate.set(false)">
        <div class="modal" (click)="$event.stopPropagation()">
          <h2>Create Team</h2>
          <form (ngSubmit)="createTeam()">
            <div class="form-group">
              <label>Team Name *</label>
              <input type="text" [(ngModel)]="newTeamName" name="name" required placeholder="e.g., Engineering Team" />
            </div>
            <div class="form-group">
              <label>Description</label>
              <textarea [(ngModel)]="newTeamDesc" name="desc" rows="2" placeholder="What is this team for?"></textarea>
            </div>
            <div class="modal-actions">
              <button type="button" class="btn btn-secondary" (click)="showCreate.set(false)">Cancel</button>
              <button type="submit" class="btn btn-primary">Create Team</button>
            </div>
          </form>
        </div>
      </div>

      <div class="modal-overlay" *ngIf="selectedTeam()" (click)="selectedTeam.set(null)">
        <div class="modal modal-lg" (click)="$event.stopPropagation()">
          <h2>{{ selectedTeam()!.name }}</h2>
          <p *ngIf="selectedTeam()!.description" style="color:#888;margin-bottom:1rem">{{ selectedTeam()!.description }}</p>

          <div class="members-section">
            <div class="members-header">
              <h3>Members</h3>
              <button class="btn btn-sm btn-primary" (click)="showAddMember.set(true)">
                <span class="material-icons">person_add</span> Add Member
              </button>
            </div>

            <div *ngIf="showAddMember()" class="add-member-form">
              <input type="email" [(ngModel)]="addMemberEmail" placeholder="Email address" />
              <select [(ngModel)]="addMemberRole">
                <option value="Member">Member</option>
                <option value="Admin">Admin</option>
                <option value="Viewer">Viewer</option>
              </select>
              <button class="btn btn-primary" (click)="addMember()">Add</button>
              <button class="btn btn-secondary" (click)="showAddMember.set(false)">Cancel</button>
            </div>

            <div *ngFor="let m of members()" class="member-row">
              <div class="member-avatar">{{ (m.user?.name || 'U').charAt(0) }}</div>
              <div class="member-info">
                <span class="member-name">{{ m.user?.name || 'User #' + m.userId }}</span>
                <span class="member-email">{{ m.user?.email || '' }}</span>
              </div>
              <span class="role-badge" [ngClass]="m.role.toLowerCase()">{{ m.role }}</span>
              <button *ngIf="m.role !== 'Owner'" class="icon-btn" (click)="removeMember(m.userId)" title="Remove">
                <span class="material-icons">close</span>
              </button>
            </div>
          </div>

          <div class="modal-actions">
            <button class="btn btn-danger" (click)="deleteTeam()">Delete Team</button>
            <button class="btn btn-secondary" (click)="selectedTeam.set(null)">Close</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .teams-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 1rem; }
    .team-card { display: flex; align-items: center; gap: 1rem; background: #1e1e2e; border: 1px solid #333; border-radius: 12px; padding: 1.2rem; cursor: pointer; transition: all 0.2s; }
    .team-card:hover { border-color: #667eea; transform: translateY(-2px); }
    .team-icon .material-icons { font-size: 40px; color: #667eea; }
    .team-info h4 { margin: 0 0 0.3rem; color: #e0e0e0; }
    .team-info p { margin: 0; color: #888; font-size: 0.85rem; }
    .team-date { color: #666; font-size: 0.8rem; }
    .modal-lg { max-width: 600px; }
    .members-header { display: flex; justify-content: space-between; align-items: center; margin: 1.5rem 0 1rem; }
    .members-header h3 { margin: 0; color: #e0e0e0; }
    .member-row { display: flex; align-items: center; gap: 0.8rem; padding: 0.8rem; border-radius: 8px; transition: background 0.2s; }
    .member-row:hover { background: #2a2a3e; }
    .member-avatar { width: 36px; height: 36px; border-radius: 50%; background: linear-gradient(135deg, #667eea, #764ba2); color: white; display: flex; align-items: center; justify-content: center; font-weight: 600; }
    .member-info { flex: 1; }
    .member-name { display: block; color: #e0e0e0; font-size: 0.9rem; }
    .member-email { color: #888; font-size: 0.8rem; }
    .role-badge { padding: 0.2rem 0.8rem; border-radius: 12px; font-size: 0.75rem; font-weight: 600; text-transform: uppercase; }
    .role-badge.owner { background: rgba(102, 126, 234, 0.2); color: #667eea; }
    .role-badge.admin { background: rgba(255, 167, 38, 0.2); color: #ffa726; }
    .role-badge.member { background: rgba(67, 233, 123, 0.2); color: #43e97b; }
    .role-badge.viewer { background: rgba(150, 150, 150, 0.2); color: #999; }
    .add-member-form { display: flex; gap: 0.5rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .add-member-form input { flex: 1; min-width: 200px; padding: 0.6rem 1rem; background: #2a2a3e; border: 1px solid #444; border-radius: 8px; color: #e0e0e0; }
    .add-member-form select { padding: 0.6rem; background: #2a2a3e; border: 1px solid #444; border-radius: 8px; color: #e0e0e0; }
    .btn-danger { background: #f44336; color: white; border: none; padding: 0.6rem 1.2rem; border-radius: 8px; cursor: pointer; }
    .btn-sm { padding: 0.4rem 0.8rem; font-size: 0.85rem; display: inline-flex; align-items: center; gap: 0.3rem; }
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
    this.api.getTeams().subscribe(t => { this.teams.set(t); this.loading.set(false); });
  }

  createTeam(): void {
    if (!this.newTeamName.trim()) return;
    this.api.createTeam({ name: this.newTeamName, description: this.newTeamDesc }).subscribe(() => {
      this.showCreate.set(false);
      this.newTeamName = '';
      this.newTeamDesc = '';
      this.toast.success('Team created');
      this.loadTeams();
    });
  }

  selectTeam(team: Team): void {
    this.selectedTeam.set(team);
    this.api.getTeam(team.id).subscribe(t => {
      this.selectedTeam.set(t);
      this.members.set(t.members || []);
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
    if (!this.selectedTeam() || !confirm('Remove this member?')) return;
    this.api.removeTeamMember(this.selectedTeam()!.id, userId).subscribe({
      next: () => { this.toast.success('Member removed'); this.selectTeam(this.selectedTeam()!); },
      error: (e) => this.toast.error(e.error?.error || 'Failed to remove member')
    });
  }

  deleteTeam(): void {
    if (!this.selectedTeam() || !confirm('Delete this team permanently?')) return;
    this.api.deleteTeam(this.selectedTeam()!.id).subscribe({
      next: () => { this.toast.success('Team deleted'); this.selectedTeam.set(null); this.loadTeams(); },
      error: (e) => this.toast.error(e.error?.error || 'Failed to delete team')
    });
  }
}
