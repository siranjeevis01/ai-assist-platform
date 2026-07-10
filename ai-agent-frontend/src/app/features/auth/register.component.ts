import { Component, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { NgIf, NgFor } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [FormsModule, RouterLink, NgIf, NgFor],
  template: `
    <div class="auth-container">
      <div class="auth-card">
        <div class="auth-header">
          <h1>AI Agent</h1>
          <p>Create your account</p>
        </div>

        <form (ngSubmit)="handleSubmit()" class="auth-form">
          <div class="error-message" *ngIf="error()">{{ error() }}</div>

          <div class="form-group">
            <label for="name">Full Name</label>
            <input type="text" id="name" [(ngModel)]="name" name="name" placeholder="Enter your full name" required />
          </div>

          <div class="form-group">
            <label for="email">Email</label>
            <input type="email" id="email" [(ngModel)]="email" name="email" placeholder="Enter your email" required />
          </div>

          <div class="form-group">
            <label for="password">Password</label>
            <input type="password" id="password" [(ngModel)]="password" name="password" placeholder="Enter your password" required (input)="checkPasswordStrength(password)" />
            <div class="password-strength" *ngIf="password">
              <div class="strength-bar">
                <div class="strength-fill" [style.width.%]="(strengthScore()/5)*100" [style.background-color]="strengthColor()"></div>
              </div>
              <div class="strength-text">Strength: <span [style.color]="strengthColor()">{{ strengthText() }}</span></div>
              <div class="password-feedback" *ngIf="feedback().length">
                <div *ngFor="let msg of feedback()" class="feedback-item">&bull; {{ msg }}</div>
              </div>
            </div>
          </div>

          <div class="form-group">
            <label for="confirmPassword">Confirm Password</label>
            <input type="password" id="confirmPassword" [(ngModel)]="confirmPassword" name="confirmPassword" placeholder="Confirm your password" required />
            <div class="error-text" *ngIf="confirmPassword && password !== confirmPassword">Passwords do not match</div>
          </div>

          <button type="submit" class="auth-btn" [disabled]="loading() || password !== confirmPassword || strengthScore() < 3">
            {{ loading() ? 'Creating account...' : 'Sign up' }}
          </button>
        </form>

        <div class="auth-footer">
          <p>Already have an account? <a routerLink="/login">Sign in</a></p>
        </div>
      </div>
    </div>
  `,
  styleUrl: './auth.component.scss'
})
export class RegisterComponent {
  name = '';
  email = '';
  password = '';
  confirmPassword = '';
  loading = signal(false);
  error = signal('');

  strengthScore = signal(0);
  feedback = signal<string[]>([]);

  constructor(
    private auth: AuthService,
    private router: Router
  ) {}

  strengthColor(): string {
    const s = this.strengthScore();
    if (s === 0) return '#e74c3c';
    if (s <= 2) return '#e67e22';
    if (s <= 4) return '#f1c40f';
    return '#2ecc71';
  }

  strengthText(): string {
    const s = this.strengthScore();
    if (s === 0) return 'Very Weak';
    if (s <= 2) return 'Weak';
    if (s <= 4) return 'Good';
    return 'Strong';
  }

  checkPasswordStrength(pw: string): void {
    let score = 0;
    const fb: string[] = [];
    if (pw.length >= 8) score++; else fb.push('At least 8 characters');
    if (/[A-Z]/.test(pw)) score++; else fb.push('One uppercase letter');
    if (/[a-z]/.test(pw)) score++; else fb.push('One lowercase letter');
    if (/[0-9]/.test(pw)) score++; else fb.push('One number');
    if (/[!@#$%^&*()_+=\-\[\]{};':"\\|,.<>\/?]/.test(pw)) score++; else fb.push('One special character');
    this.strengthScore.set(score);
    this.feedback.set(fb);
  }

  async handleSubmit(): Promise<void> {
    if (!this.name || !this.email || !this.password) return;
    if (this.password !== this.confirmPassword) { this.error.set('Passwords do not match'); return; }
    if (this.strengthScore() < 3) { this.error.set('Please choose a stronger password'); return; }

    this.loading.set(true);
    this.error.set('');

    const success = await this.auth.register({ name: this.name, email: this.email, password: this.password });
    if (success) {
      this.router.navigate(['/dashboard']);
    } else {
      this.error.set('Registration failed. Please try again.');
    }
    this.loading.set(false);
  }
}
