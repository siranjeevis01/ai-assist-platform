importScripts('https://www.gstatic.com/firebasejs/10.12.0/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/10.12.0/firebase-messaging-compat.js');

firebase.initializeApp({
  apiKey: 'YOUR_API_KEY',
  authDomain: 'aiagent-siranjeevis01.firebaseapp.com',
  projectId: 'aiagent-siranjeevis01',
  storageBucket: 'aiagent-siranjeevis01.firebasestorage.app',
  messagingSenderId: 'YOUR_MESSAGING_SENDER_ID',
  appId: 'YOUR_APP_ID',
});

const messaging = firebase.messaging();

messaging.onBackgroundMessage((payload) => {
  const notificationTitle = payload.notification?.title || 'AI Agent';
  const notificationOptions = {
    body: payload.notification?.body || '',
    icon: '/assets/icons/icon-192x192.png',
    data: payload.data,
  };
  self.registration.showNotification(notificationTitle, notificationOptions);
});
