import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="landing">
      <nav class="navbar">
        <div class="nav-container">
          <div class="nav-brand">
            <span class="material-icons brand-icon">smart_toy</span>
            <span class="brand-text">AI Agent</span>
          </div>
          <div class="nav-links">
            <a routerLink="/login" class="nav-link">Log In</a>
            <a routerLink="/register" class="nav-btn">Get Started</a>
          </div>
        </div>
      </nav>

      <section class="hero">
        <div class="hero-bg"></div>
        <div class="hero-content">
          <div class="hero-badge">Powered by Gemini 2.0 Flash</div>
          <h1>AI Agent</h1>
          <p class="hero-subtitle">Your Intelligent Work Operating System</p>
          <p class="hero-desc">Automate tasks, manage your calendar, handle emails, and collaborate with your team — all through natural conversation.</p>
          <div class="hero-actions">
            <a routerLink="/register" class="btn-hero-primary">
              <span class="material-icons">rocket_launch</span>
              Get Started Free
            </a>
            <a routerLink="/login" class="btn-hero-secondary">
              <span class="material-icons">play_circle</span>
              Watch Demo
            </a>
          </div>
        </div>
      </section>

      <section class="features" id="features">
        <div class="section-container">
          <div class="section-header">
            <span class="section-badge">Features</span>
            <h2>Everything you need to stay productive</h2>
            <p>Six powerful integrations working together seamlessly</p>
          </div>
          <div class="features-grid">
            <div class="feature-card">
              <div class="feature-icon" style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%)">
                <span class="material-icons">smart_toy</span>
              </div>
              <h3>Chat with AI</h3>
              <p>Natural language commands for tasks, calendar, and emails</p>
            </div>
            <div class="feature-card">
              <div class="feature-icon" style="background: linear-gradient(135deg, #f5576c 0%, #ff7a5a 100%)">
                <span class="material-icons">bolt</span>
              </div>
              <h3>Smart Automation</h3>
              <p>10+ pre-built automation templates that save hours daily</p>
            </div>
            <div class="feature-card">
              <div class="feature-icon" style="background: linear-gradient(135deg, #43e97b 0%, #38f9d7 100%)">
                <span class="material-icons">email</span>
              </div>
              <h3>Email Integration</h3>
              <p>Read, compose, and manage Gmail directly from chat</p>
            </div>
            <div class="feature-card">
              <div class="feature-icon" style="background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%)">
                <span class="material-icons">calendar_today</span>
              </div>
              <h3>Calendar Sync</h3>
              <p>Google Calendar integration with voice scheduling</p>
            </div>
            <div class="feature-card">
              <div class="feature-icon" style="background: linear-gradient(135deg, #ffa726 0%, #ff7043 100%)">
                <span class="material-icons">group</span>
              </div>
              <h3>Team Collaboration</h3>
              <p>RBAC teams with shared tasks and documents</p>
            </div>
            <div class="feature-card">
              <div class="feature-icon" style="background: linear-gradient(135deg, #ab47bc 0%, #7c4dff 100%)">
                <span class="material-icons">mic</span>
              </div>
              <h3>Voice Assistant</h3>
              <p>Speak commands using Gemini 2.0 Flash speech-to-text</p>
            </div>
          </div>
        </div>
      </section>

      <section class="how-it-works">
        <div class="section-container">
          <div class="section-header">
            <span class="section-badge">How It Works</span>
            <h2>Three steps to productivity</h2>
            <p>Get started in minutes, not hours</p>
          </div>
          <div class="steps-grid">
            <div class="step-card">
              <div class="step-number">1</div>
              <div class="step-icon">
                <span class="material-icons">link</span>
              </div>
              <h3>Connect</h3>
              <p>Link Google, Telegram, Trello in one click</p>
            </div>
            <div class="step-connector">
              <span class="material-icons">arrow_forward</span>
            </div>
            <div class="step-card">
              <div class="step-number">2</div>
              <div class="step-icon">
                <span class="material-icons">chat</span>
              </div>
              <h3>Ask</h3>
              <p>Type or speak naturally: 'Schedule meeting tomorrow at 3pm'</p>
            </div>
            <div class="step-connector">
              <span class="material-icons">arrow_forward</span>
            </div>
            <div class="step-card">
              <div class="step-number">3</div>
              <div class="step-icon">
                <span class="material-icons">check_circle</span>
              </div>
              <h3>Done</h3>
              <p>AI handles the rest automatically</p>
            </div>
          </div>
        </div>
      </section>

      <section class="stats">
        <div class="section-container">
          <div class="stats-grid">
            <div class="stat-card">
              <div class="stat-value">10+</div>
              <div class="stat-label">Automation Templates</div>
            </div>
            <div class="stat-card">
              <div class="stat-value">10+</div>
              <div class="stat-label">NLP Intents</div>
            </div>
            <div class="stat-card">
              <div class="stat-value">3</div>
              <div class="stat-label">Integrations</div>
            </div>
            <div class="stat-card">
              <div class="stat-value">100%</div>
              <div class="stat-label">Free</div>
            </div>
          </div>
        </div>
      </section>

      <section class="cta">
        <div class="section-container">
          <div class="cta-content">
            <h2>Ready to boost your productivity?</h2>
            <p>Join now and let AI handle the busy work while you focus on what matters.</p>
            <a routerLink="/register" class="btn-cta">
              <span class="material-icons">rocket_launch</span>
              Start Free
            </a>
          </div>
        </div>
      </section>

      <footer class="footer">
        <div class="footer-container">
          <div class="footer-brand">
            <span class="material-icons brand-icon">smart_toy</span>
            <span>AI Agent</span>
          </div>
          <div class="footer-links">
            <a routerLink="/login">Log In</a>
            <a routerLink="/register">Sign Up</a>
          </div>
          <div class="footer-copy">
            &copy; 2026 AI Agent. All rights reserved.
          </div>
        </div>
      </footer>
    </div>
  `,
  styles: [`
    :host {
      display: block;
      background: #0d1117;
      color: #e6edf3;
      min-height: 100vh;
    }

    .landing {
      font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
    }

    .navbar {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      z-index: 100;
      background: rgba(13, 17, 23, 0.85);
      backdrop-filter: blur(12px);
      border-bottom: 1px solid rgba(255, 255, 255, 0.06);
    }

    .nav-container {
      max-width: 1200px;
      margin: 0 auto;
      padding: 0 24px;
      height: 64px;
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .nav-brand {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .brand-icon {
      font-size: 28px;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
    }

    .brand-text {
      font-size: 20px;
      font-weight: 700;
      color: #fff;
    }

    .nav-links {
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .nav-link {
      color: #8b949e;
      text-decoration: none;
      font-size: 14px;
      font-weight: 500;
      transition: color 0.2s;
    }

    .nav-link:hover {
      color: #fff;
    }

    .nav-btn {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: #fff;
      text-decoration: none;
      padding: 8px 20px;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 600;
      transition: transform 0.2s, box-shadow 0.2s;
    }

    .nav-btn:hover {
      transform: translateY(-1px);
      box-shadow: 0 4px 15px rgba(102, 126, 234, 0.4);
    }

    .hero {
      position: relative;
      padding: 160px 24px 100px;
      text-align: center;
      overflow: hidden;
    }

    .hero-bg {
      position: absolute;
      inset: 0;
      background: radial-gradient(ellipse at 50% 0%, rgba(102, 126, 234, 0.15) 0%, transparent 60%);
      pointer-events: none;
    }

    .hero-content {
      position: relative;
      max-width: 800px;
      margin: 0 auto;
    }

    .hero-badge {
      display: inline-block;
      padding: 6px 16px;
      background: rgba(102, 126, 234, 0.1);
      border: 1px solid rgba(102, 126, 234, 0.2);
      border-radius: 20px;
      font-size: 13px;
      font-weight: 500;
      color: #667eea;
      margin-bottom: 24px;
    }

    .hero h1 {
      font-size: 72px;
      font-weight: 800;
      line-height: 1.1;
      margin-bottom: 16px;
      background: linear-gradient(135deg, #fff 0%, #a8b2d1 100%);
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
    }

    .hero-subtitle {
      font-size: 24px;
      font-weight: 600;
      color: #667eea;
      margin-bottom: 20px;
    }

    .hero-desc {
      font-size: 18px;
      color: #8b949e;
      max-width: 600px;
      margin: 0 auto 40px;
      line-height: 1.7;
    }

    .hero-actions {
      display: flex;
      gap: 16px;
      justify-content: center;
      flex-wrap: wrap;
    }

    .btn-hero-primary {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 14px 32px;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: #fff;
      text-decoration: none;
      border-radius: 12px;
      font-size: 16px;
      font-weight: 600;
      transition: transform 0.2s, box-shadow 0.2s;
    }

    .btn-hero-primary:hover {
      transform: translateY(-2px);
      box-shadow: 0 8px 25px rgba(102, 126, 234, 0.4);
    }

    .btn-hero-secondary {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 14px 32px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      color: #e6edf3;
      text-decoration: none;
      border-radius: 12px;
      font-size: 16px;
      font-weight: 600;
      transition: background 0.2s, border-color 0.2s;
    }

    .btn-hero-secondary:hover {
      background: rgba(255, 255, 255, 0.08);
      border-color: rgba(255, 255, 255, 0.2);
    }

    .section-container {
      max-width: 1200px;
      margin: 0 auto;
      padding: 0 24px;
    }

    .section-header {
      text-align: center;
      margin-bottom: 60px;
    }

    .section-badge {
      display: inline-block;
      padding: 4px 12px;
      background: rgba(102, 126, 234, 0.1);
      border: 1px solid rgba(102, 126, 234, 0.2);
      border-radius: 12px;
      font-size: 12px;
      font-weight: 600;
      color: #667eea;
      text-transform: uppercase;
      letter-spacing: 1px;
      margin-bottom: 16px;
    }

    .section-header h2 {
      font-size: 40px;
      font-weight: 700;
      color: #fff;
      margin-bottom: 12px;
    }

    .section-header p {
      font-size: 18px;
      color: #8b949e;
    }

    .features {
      padding: 100px 0;
    }

    .features-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 24px;
    }

    .feature-card {
      background: rgba(30, 30, 46, 0.5);
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 16px;
      padding: 32px;
      transition: transform 0.3s, border-color 0.3s, box-shadow 0.3s;
    }

    .feature-card:hover {
      transform: translateY(-4px);
      border-color: rgba(102, 126, 234, 0.3);
      box-shadow: 0 8px 30px rgba(0, 0, 0, 0.3);
    }

    .feature-icon {
      width: 52px;
      height: 52px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      margin-bottom: 20px;
    }

    .feature-icon .material-icons {
      font-size: 26px;
      color: #fff;
    }

    .feature-card h3 {
      font-size: 18px;
      font-weight: 600;
      color: #fff;
      margin-bottom: 8px;
    }

    .feature-card p {
      font-size: 14px;
      color: #8b949e;
      line-height: 1.6;
    }

    .how-it-works {
      padding: 100px 0;
      background: rgba(255, 255, 255, 0.02);
    }

    .steps-grid {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 24px;
    }

    .step-card {
      background: rgba(30, 30, 46, 0.5);
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 16px;
      padding: 40px 32px;
      text-align: center;
      flex: 1;
      max-width: 300px;
      position: relative;
      transition: transform 0.3s, border-color 0.3s;
    }

    .step-card:hover {
      transform: translateY(-4px);
      border-color: rgba(102, 126, 234, 0.3);
    }

    .step-number {
      position: absolute;
      top: -16px;
      left: 50%;
      transform: translateX(-50%);
      width: 32px;
      height: 32px;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 14px;
      font-weight: 700;
      color: #fff;
    }

    .step-icon {
      margin-bottom: 20px;
    }

    .step-icon .material-icons {
      font-size: 40px;
      color: #667eea;
    }

    .step-card h3 {
      font-size: 20px;
      font-weight: 600;
      color: #fff;
      margin-bottom: 8px;
    }

    .step-card p {
      font-size: 14px;
      color: #8b949e;
      line-height: 1.6;
    }

    .step-connector {
      color: rgba(102, 126, 234, 0.4);
      flex-shrink: 0;
    }

    .step-connector .material-icons {
      font-size: 28px;
    }

    .stats {
      padding: 80px 0;
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 24px;
    }

    .stat-card {
      background: rgba(30, 30, 46, 0.5);
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 16px;
      padding: 32px;
      text-align: center;
      transition: transform 0.3s, border-color 0.3s;
    }

    .stat-card:hover {
      transform: translateY(-4px);
      border-color: rgba(102, 126, 234, 0.3);
    }

    .stat-value {
      font-size: 42px;
      font-weight: 800;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
      margin-bottom: 8px;
    }

    .stat-label {
      font-size: 14px;
      color: #8b949e;
      font-weight: 500;
    }

    .cta {
      padding: 100px 0;
    }

    .cta-content {
      text-align: center;
      background: linear-gradient(135deg, rgba(102, 126, 234, 0.1) 0%, rgba(118, 75, 162, 0.1) 100%);
      border: 1px solid rgba(102, 126, 234, 0.2);
      border-radius: 24px;
      padding: 80px 40px;
    }

    .cta-content h2 {
      font-size: 36px;
      font-weight: 700;
      color: #fff;
      margin-bottom: 16px;
    }

    .cta-content p {
      font-size: 18px;
      color: #8b949e;
      margin-bottom: 32px;
      max-width: 500px;
      margin-left: auto;
      margin-right: auto;
    }

    .btn-cta {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 16px 36px;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: #fff;
      text-decoration: none;
      border-radius: 12px;
      font-size: 18px;
      font-weight: 600;
      transition: transform 0.2s, box-shadow 0.2s;
    }

    .btn-cta:hover {
      transform: translateY(-2px);
      box-shadow: 0 8px 25px rgba(102, 126, 234, 0.4);
    }

    .footer {
      border-top: 1px solid rgba(255, 255, 255, 0.06);
      padding: 40px 0;
    }

    .footer-container {
      max-width: 1200px;
      margin: 0 auto;
      padding: 0 24px;
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .footer-brand {
      display: flex;
      align-items: center;
      gap: 8px;
      font-weight: 600;
      color: #fff;
    }

    .footer-brand .brand-icon {
      font-size: 22px;
    }

    .footer-links {
      display: flex;
      gap: 24px;
    }

    .footer-links a {
      color: #8b949e;
      text-decoration: none;
      font-size: 14px;
      transition: color 0.2s;
    }

    .footer-links a:hover {
      color: #fff;
    }

    .footer-copy {
      color: #484f58;
      font-size: 13px;
    }

    @media (max-width: 900px) {
      .hero h1 {
        font-size: 48px;
      }

      .hero-subtitle {
        font-size: 20px;
      }

      .features-grid {
        grid-template-columns: repeat(2, 1fr);
      }

      .steps-grid {
        flex-direction: column;
      }

      .step-connector {
        transform: rotate(90deg);
      }

      .stats-grid {
        grid-template-columns: repeat(2, 1fr);
      }

      .section-header h2 {
        font-size: 30px;
      }
    }

    @media (max-width: 600px) {
      .hero h1 {
        font-size: 36px;
      }

      .features-grid {
        grid-template-columns: 1fr;
      }

      .stats-grid {
        grid-template-columns: 1fr 1fr;
      }

      .footer-container {
        flex-direction: column;
        gap: 16px;
        text-align: center;
      }

      .cta-content {
        padding: 48px 24px;
      }

      .cta-content h2 {
        font-size: 26px;
      }
    }
  `]
})
export class LandingComponent {}
