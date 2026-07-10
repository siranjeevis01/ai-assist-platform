# AI Agent - Complete Deployment Guide

## Architecture
- **Frontend**: Firebase Hosting (free) → aiagent-siranjeevis01.web.app
- **Backend**: Render.com (free tier) → ai-agent-backend.onrender.com
- **Database**: Render.com MySQL (free tier)

## Step 1: Deploy Backend to Render.com

1. Push this repo to GitHub:
```bash
cd D:\Siranjeevi\AIAssist
git init
git add -A
git commit -m "AI Agent - Production Ready"
git remote add origin https://github.com/YOUR_USERNAME/AIAssist.git
git push -u origin main
```

2. Go to https://render.com → New → Blueprint → Select your repo
3. Render auto-detects `render.yaml` and creates:
   - Backend service (ai-agent-backend)
   - Frontend service (ai-agent-frontend)
   - MySQL database (ai-agent-db)

4. After deployment, go to the backend service → Environment tab and add:
   - `Gemini__ApiKey` = your Gemini key
   - `Google__ClientId` = your Google OAuth client ID
   - `Google__ClientSecret` = your Google OAuth client secret
   - `Messaging__Telegram__BotToken` = your Telegram bot token
   - `Messaging__WhatsApp__AccessToken` = your WhatsApp token
   - `Trello__ApiKey` = your Trello API key
   - `Trello__ApiSecret` = your Trello API secret
   - `OpenAI__ApiKey` = your OpenAI key

## Step 2: Update Google OAuth for Production

1. Go to https://console.cloud.google.com/apis/credentials
2. Edit your OAuth 2.0 client
3. Add these Authorized redirect URIs:
   - https://ai-agent-backend.onrender.com/api/google/callback
   - https://ai-agent-backend.onrender.com/swagger/oauth2-redirect.html
4. Save changes

## Step 3: Deploy Frontend to Firebase

```bash
cd D:\Siranjeevi\AIAssist

# Update environment.prod.ts with your Render backend URL
# Then build
cd ai-agent-frontend
npm run build -- --configuration production

# Deploy to Firebase
cd ..
firebase login
firebase deploy --only hosting
```

## Step 4: Fix WhatsApp (Business Account Locked)

Your WhatsApp Business Account is LOCKED. To fix:
1. Go to https://business.facebook.com/settings/
2. Check for any compliance issues
3. Or create a new WhatsApp Business Account
4. Generate a new access token

## Step 5: Generate Trello Access Token

1. Go to https://trello.com/power-ups/admin
2. Click "New" → Create a Power-Up
3. Generate an API key
4. Use the API key to authorize and get access token:
   https://trello.com/1/authorize?key=YOUR_KEY&name=AiAgent&scope=read,write&expiration=never&response_type=token

## Step 6: Gmail App Password

1. Go to https://myaccount.google.com/apppasswords
2. Generate a new app password for "Mail"
3. Use this as Smtp__Password
