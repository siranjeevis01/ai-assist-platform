import { createRequire } from 'module';
const require = createRequire(import.meta.url);
require('dotenv').config();

import makeWASocket, {
    useMultiFileAuthState,
    fetchLatestBaileysVersion,
    DisconnectReason,
    delay,
    proto
} from '@whiskeysockets/baileys';
import express from 'express';
import bodyParser from 'body-parser';
import fetch from 'node-fetch';
import qrcode from 'qrcode-terminal';
import net from 'net';
import fs from 'fs';
import { exec } from 'child_process';

const app = express();
app.use(bodyParser.json());

const BACKEND_URL = process.env.BACKEND_URL || 'http://backend:5000';
const PORT = parseInt(process.env.WHATSAPP_PORT) || 3001;
const BACKEND_RETRY_ATTEMPTS = parseInt(process.env.BACKEND_RETRY_ATTEMPTS) || 10;
const BACKEND_RETRY_DELAY = parseInt(process.env.BACKEND_RETRY_DELAY) || 5000;

let sock = null;
let qrCode = null;
let isConnected = false;
let backendReady = false;

// Enhanced backend connection checker with retry logic
async function checkBackendConnection(maxRetries = BACKEND_RETRY_ATTEMPTS, retryDelay = BACKEND_RETRY_DELAY) {
    let retries = 0;
    
    console.log(`🔍 Checking backend connection to: ${BACKEND_URL}`);
    
    while (retries < maxRetries) {
        try {
            console.log(`📡 Attempt ${retries + 1}/${maxRetries} to connect to backend...`);
            
            const response = await fetch(`${BACKEND_URL}/health`, {
                timeout: 10000
            });
            
            if (response.ok) {
                const health = await response.json();
                console.log('✅ Backend is ready! Health status:', health.status);
                backendReady = true;
                return true;
            } else {
                console.log(`❌ Backend not ready yet (HTTP ${response.status}). Retrying...`);
            }
        } catch (error) {
            console.log(`❌ Backend connection failed (attempt ${retries + 1}): ${error.message}`);
            
            if (error.code === 'ENOTFOUND' || error.code === 'ECONNREFUSED') {
                console.log('🌐 DNS/Connection issue - backend service might still be starting...');
            }
        }
        
        retries++;
        if (retries < maxRetries) {
            console.log(`⏳ Waiting ${retryDelay/1000} seconds before next attempt...`);
            await delay(retryDelay);
        }
    }
    
    console.error('🚫 Backend connection failed after all retries. WhatsApp bot will start but messages may fail.');
    return false;
}

// Enhanced message processing with retry logic
async function processEnhancedMessage(from, text, messageId, maxRetries = 3) {
    let retries = 0;
    
    if (!backendReady) {
        console.log('⚠️ Backend not ready, skipping message processing');
        return { error: 'Backend not ready' };
    }
    
    while (retries < maxRetries) {
        try {
            console.log(`📩 Processing message from: ${from} - ${text}`);
            
            if (from.includes('@g.us')) {
                console.log(`Ignoring group message from: ${from}`);
                return { status: "Ignored", reason: "Group message" };
            }
            
            const phone = from.replace(/@s\.whatsapp\.net$/, '');
            
            const response = await fetch(`${BACKEND_URL}/webhooks/whatsapp`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json',
                    'User-Agent': 'WhatsApp-Bot/1.0'
                },
                body: JSON.stringify({
                    phone: phone,
                    text: text || '',
                    messageId: messageId,
                    timestamp: new Date().toISOString(),
                    messageType: 'text'
                }),
                timeout: 30000
            });

            if (response.ok) {
                const result = await response.json();
                console.log(`✅ Successfully processed message from ${from}`);
                return result;
            } else {
                const errorText = await response.text();
                console.error(`❌ Backend error (attempt ${retries + 1}/${maxRetries}): ${response.status} - ${errorText}`);
                
                if (response.status >= 500) {
                    // Server error, retry
                    retries++;
                    if (retries < maxRetries) {
                        await delay(2000 * retries);
                        continue;
                    }
                } else {
                    // Client error, don't retry
                    break;
                }
            }
        } catch (error) {
            console.error(`❌ Processing error (attempt ${retries + 1}/${maxRetries}):`, error.message);
            
            if (error.code === 'ENOTFOUND' || error.code === 'ECONNREFUSED') {
                backendReady = false;
                console.log('🔧 Backend connection lost, will retry connection...');
                await checkBackendConnection(3, 2000); // Quick retry
            }
            
            retries++;
            if (retries < maxRetries) {
                await delay(2000 * retries);
            }
        }
    }
    
    console.error(`🚫 Failed to process message after ${maxRetries} attempts`);
    return { error: 'Processing failed after retries' };
}

// Enhanced send function with retry logic
async function sendEnhancedMessage(to, message, options = {}) {
    const maxRetries = 3;
    let retries = 0;
    
    if (!sock) {
        throw new Error('WhatsApp not connected');
    }
    
    while (retries < maxRetries) {
        try {
            if (options.media) {
                return await sendMediaMessage(to, options.media.url, options.media.caption, options.media.type);
            } else if (options.buttons) {
                return await sendButtonMessage(to, message, options.buttons);
            } else if (options.template) {
                return await sendTemplateMessage(to, options.template.name, options.template.params);
            } else {
                return await sendTextMessage(to, message);
            }
        } catch (error) {
            retries++;
            console.error(`Send error (attempt ${retries}/${maxRetries}):`, error.message);
            
            if (retries === maxRetries) {
                throw error;
            }
            await delay(2000 * retries);
        }
    }
}

async function sendTextMessage(to, text) {
    if (!sock) throw new Error('WhatsApp not connected');
    
    const jid = to.includes('@s.whatsapp.net') ? to : `${to}@s.whatsapp.net`;
    await sock.sendMessage(jid, { text: text });
    return { success: true, type: 'text' };
}

async function sendMediaMessage(to, url, caption = '', type = 'image') {
    if (!sock) throw new Error('WhatsApp not connected');
    
    const jid = to.includes('@s.whatsapp.net') ? to : `${to}@s.whatsapp.net`;
    
    let mimetype = 'image/jpeg';
    if (url.includes('.png')) mimetype = 'image/png';
    if (url.includes('.gif')) mimetype = 'image/gif';
    if (url.includes('.mp4')) mimetype = 'video/mp4';
    if (url.includes('.pdf')) mimetype = 'application/pdf';

    const messageOptions = {
        [type]: { url: url },
        caption: caption || '',
        mimetype: mimetype
    };

    await sock.sendMessage(jid, messageOptions);
    return { success: true, type: 'media' };
}

async function sendButtonMessage(to, body, buttons) {
    if (!sock) throw new Error('WhatsApp not connected');
    
    const jid = to.includes('@s.whatsapp.net') ? to : `${to}@s.whatsapp.net`;
    
    const buttonMessage = {
        text: body,
        footer: 'AI Agent',
        buttons: buttons.map((btn, index) => ({
            buttonId: `btn${index + 1}`,
            buttonText: { displayText: btn }
        })),
        headerType: 1
    };

    await sock.sendMessage(jid, buttonMessage);
    return { success: true, type: 'buttons' };
}

async function sendTemplateMessage(to, templateName, parameters = []) {
    if (!sock) throw new Error('WhatsApp not connected');
    
    const jid = to.includes('@s.whatsapp.net') ? to : `${to}@s.whatsapp.net`;
    
    // Simulate template message with formatted text
    const message = `🔔 ${templateName}\n\n${parameters.join('\n')}`;
    
    await sock.sendMessage(jid, { text: message });
    return { success: true, type: 'template' };
}

async function connectWhatsApp() {
    try {
        console.log('🔧 Waiting for backend to be ready before starting WhatsApp...');
        await checkBackendConnection();
        
        console.log('🔧 Initializing WhatsApp connection...');
        const { state, saveCreds } = await useMultiFileAuthState('./auth_info');
        const { version } = await fetchLatestBaileysVersion();

        sock = makeWASocket({
            version: version,
            auth: state,
            printQRInTerminal: false,
            getMessage: async () => undefined,
            connectTimeoutMs: 60000,
            keepAliveIntervalMs: 30000
        });

        sock.ev.on('creds.update', saveCreds);

        sock.ev.on('connection.update', async (update) => {
            const { connection, lastDisconnect, qr } = update;
        
            if (qr) {
                console.log('📱 QR code received');
                qrCode = qr;
                
                // Generate QR for terminal
                qrcode.generate(qr, { small: true });
            }

            if (connection === 'close') {
                const shouldReconnect =
                    lastDisconnect?.error?.output?.statusCode !== DisconnectReason.loggedOut;
                console.log(`❌ Connection closed. Reconnect: ${shouldReconnect}`);
                isConnected = false;
                
                if (shouldReconnect) {
                    await delay(5000);
                    connectWhatsApp();
                } else {
                    console.log('🚫 Logged out. Delete auth_info folder and restart.');
                    if (fs.existsSync('./auth_info')) {
                        fs.rmSync('./auth_info', { recursive: true });
                    }
                }
            } else if (connection === 'open') {
                console.log('✅ WhatsApp Connected Successfully!');
                isConnected = true;
                qrCode = null;
                
                // Send welcome message if backend is ready
                if (backendReady) {
                    try {
                        console.log('🎉 Sending system ready notification...');
                        // This could be enhanced to send to specific users
                    } catch (error) {
                        console.log('Note: Could not send ready notification');
                    }
                }
            }
        });

        sock.ev.on('messages.upsert', async ({ messages }) => {
            const msg = messages[0];
            if (!msg.message || msg.key.fromMe) return;

            const from = msg.key.remoteJid;
            const messageId = msg.key.id;
            const text =
                msg.message.conversation ||
                msg.message.extendedTextMessage?.text ||
                msg.message.buttonsResponseMessage?.selectedDisplayText ||
                '';

            console.log(`📩 Message from ${from}: ${text}`);

            try {
                await processEnhancedMessage(from, text, messageId);
            } catch (err) {
                console.error('❌ Message processing error:', err.message);
                
                // Try to send error message to user
                try {
                    if (sock && isConnected) {
                        await sock.sendMessage(from, { 
                            text: '⚠️ Sorry, I encountered an error processing your message. Please try again.' 
                        });
                    }
                } catch (sendError) {
                    console.error('Failed to send error message:', sendError.message);
                }
            }
        });

        // Periodic backend health check
        setInterval(async () => {
            if (!backendReady) {
                console.log('🩺 Periodic backend health check...');
                await checkBackendConnection(2, 2000);
            }
        }, 60000); // Check every minute

    } catch (error) {
        console.error('❌ Connection error:', error.message);
        await delay(10000);
        connectWhatsApp();
    }
}

// Enhanced Express endpoints
app.get('/health', (req, res) => {
    res.json({ 
        status: isConnected ? 'connected' : 'disconnected',
        backendStatus: backendReady ? 'connected' : 'disconnected',
        backendUrl: BACKEND_URL,
        qrAvailable: !!qrCode,
        timestamp: new Date().toISOString(),
        service: 'whatsapp-bot'
    });
});

app.get('/qr', (req, res) => {
    if (qrCode) {
        res.json({ qr: qrCode });
    } else {
        res.json({ 
            status: isConnected ? 'connected' : 'disconnected',
            message: isConnected ? 'WhatsApp is connected' : 'No QR code available',
            backendReady: backendReady
        });
    }
});

app.get('/status', (req, res) => {
    res.json({
        whatsapp: isConnected ? 'connected' : 'disconnected',
        backend: backendReady ? 'connected' : 'disconnected',
        qrAvailable: !!qrCode,
        backendUrl: BACKEND_URL,
        timestamp: new Date().toISOString()
    });
});

app.post('/send', async (req, res) => {
    if (!isConnected) {
        return res.status(503).json({ error: 'WhatsApp not connected' });
    }
    
    const { to, text, options } = req.body;
    if (!to || !text) {
        return res.status(400).json({ error: 'Missing recipient or message text' });
    }

    try {
        const result = await sendEnhancedMessage(to, text, options);
        res.json(result);
    } catch (error) {
        console.error('Send error:', error);
        res.status(500).json({ error: error.message });
    }
});

app.post('/send-media', async (req, res) => {
    if (!isConnected) {
        return res.status(503).json({ error: 'WhatsApp not connected' });
    }
    
    const { to, url, caption, type = 'image' } = req.body;
    if (!to || !url) {
        return res.status(400).json({ error: 'Missing recipient or URL' });
    }

    try {
        const result = await sendMediaMessage(to, url, caption, type);
        res.json(result);
    } catch (error) {
        console.error('Media send error:', error);
        res.status(500).json({ error: error.message });
    }
});

app.post('/send-buttons', async (req, res) => {
    if (!isConnected) {
        return res.status(503).json({ error: 'WhatsApp not connected' });
    }
    
    const { to, body, buttons } = req.body;
    if (!to || !body || !buttons) {
        return res.status(400).json({ error: 'Missing required fields' });
    }

    try {
        const result = await sendButtonMessage(to, body, buttons);
        res.json(result);
    } catch (error) {
        console.error('Button message error:', error);
        res.status(500).json({ error: error.message });
    }
});

// Backend connectivity test endpoint
app.get('/test-backend', async (req, res) => {
    try {
        const response = await fetch(`${BACKEND_URL}/health`);
        const health = await response.json();
        res.json({
            backendStatus: 'connected',
            backendHealth: health,
            backendUrl: BACKEND_URL
        });
    } catch (error) {
        res.status(503).json({
            backendStatus: 'disconnected',
            error: error.message,
            backendUrl: BACKEND_URL
        });
    }
});

// Start server
app.listen(PORT, '0.0.0.0', () => {
    console.log(`🚀 WhatsApp bot API running on port ${PORT}`);
    console.log(`📞 Backend URL: ${BACKEND_URL}`);
    console.log(`🔄 Backend retry attempts: ${BACKEND_RETRY_ATTEMPTS}`);
    console.log(`⏱️ Backend retry delay: ${BACKEND_RETRY_DELAY}ms`);
    
    // Start WhatsApp connection
    connectWhatsApp();
});

// Graceful shutdown
process.on('SIGINT', () => {
    console.log('🛑 Shutting down gracefully...');
    process.exit(0);
});

process.on('SIGTERM', () => {
    console.log('🛑 Received SIGTERM, shutting down...');
    process.exit(0);
});