const { spawn } = require('child_process');
const fs = require('fs');

// Clear previous auth data
if (fs.existsSync('./auth_info')) {
  console.log('🧹 Clearing previous WhatsApp authentication...');
  fs.rmSync('./auth_info', { recursive: true });
}

console.log('🚀 Starting WhatsApp Bot with fresh authentication...');
console.log('📱 Please be ready to scan the QR code immediately when it appears!');
console.log('⏰ You have 45 seconds to scan each QR code');
console.log('📋 Instructions:');
console.log('   1. Open WhatsApp on your phone');
console.log('   2. Tap Settings → Linked Devices → Link a Device');
console.log('   3. Scan the QR code when it appears');
console.log('   4. Wait for connection confirmation');

const bot = spawn('node', ['bot.js'], {
  stdio: 'inherit',
  env: { ...process.env, NODE_ENV: 'development' }
});

bot.on('close', (code) => {
  console.log(`🤖 Bot process exited with code ${code}`);
  if (code !== 0) {
    console.log('🔄 Restarting bot in 5 seconds...');
    setTimeout(() => {
      console.log('🔄 Attempting to restart...');
      if (fs.existsSync('./auth_info')) {
        fs.rmSync('./auth_info', { recursive: true });
      }
      require('./bot.js');
    }, 5000);
  }
});

// Handle process termination
process.on('SIGINT', () => {
  console.log('🛑 Received shutdown signal...');
  bot.kill();
  process.exit(0);
});