// src/pages/Messages.jsx
import React, { useState, useEffect, useRef } from 'react';
import { Send, Bot, User } from 'lucide-react';
import { messageService } from '../services/api';

export default function Messages() {
  const [messages, setMessages] = useState([]);
  const [newMessage, setNewMessage] = useState('');
  const [loading, setLoading] = useState(false);
  const messagesEndRef = useRef(null);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  const sendMessage = async (e) => {
    e.preventDefault();
    if (!newMessage.trim()) return;

    const userMessage = { text: newMessage, sender: 'user', timestamp: new Date() };
    setMessages(prev => [...prev, userMessage]);
    setNewMessage('');
    setLoading(true);

    try {
      const response = await messageService.send(newMessage);
      const botMessage = { 
        text: response.result || response.message || 'I received your message!', 
        sender: 'bot', 
        timestamp: new Date() 
      };
      setMessages(prev => [...prev, botMessage]);
    } catch (error) {
      const errorMessage = { 
        text: 'Sorry, I encountered an error. Please try again.', 
        sender: 'bot', 
        timestamp: new Date() 
      };
      setMessages(prev => [...prev, errorMessage]);
    } finally {
      setLoading(false);
    }
  };

  const quickCommands = [
    'Schedule meeting tomorrow at 3 PM',
    'Create task for project report',
    'Show my calendar',
    'Check my emails'
  ];

  return (
    <div className="messages-page">
      <div className="page-header">
        <div className="header-content">
          <h1>AI Assistant</h1>
          <p>Chat with your AI assistant</p>
        </div>
      </div>

      <div className="chat-container">
        <div className="chat-messages">
          {messages.length === 0 ? (
            <div className="welcome-message">
              <Bot size={48} />
              <h3>Hello! I'm your AI Assistant</h3>
              <p>I can help you with:</p>
              <ul>
                <li>📅 Scheduling meetings and events</li>
                <li>✅ Creating and managing tasks</li>
                <li>🔔 Setting reminders</li>
                <li>📧 Checking and sending emails</li>
              </ul>
              <p>Try one of these commands or ask me anything!</p>
              
              <div className="quick-commands">
                {quickCommands.map((command, index) => (
                  <button
                    key={index}
                    className="quick-command"
                    onClick={() => setNewMessage(command)}
                  >
                    {command}
                  </button>
                ))}
              </div>
            </div>
          ) : (
            messages.map((message, index) => (
              <div key={index} className={`message ${message.sender}`}>
                <div className="message-avatar">
                  {message.sender === 'bot' ? <Bot size={20} /> : <User size={20} />}
                </div>
                <div className="message-content">
                  <div className="message-text">{message.text}</div>
                  <div className="message-time">
                    {message.timestamp.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                  </div>
                </div>
              </div>
            ))
          )}
          {loading && (
            <div className="message bot">
              <div className="message-avatar">
                <Bot size={20} />
              </div>
              <div className="message-content">
                <div className="typing-indicator">
                  <span></span>
                  <span></span>
                  <span></span>
                </div>
              </div>
            </div>
          )}
          <div ref={messagesEndRef} />
        </div>

        <form onSubmit={sendMessage} className="chat-input">
          <input
            type="text"
            value={newMessage}
            onChange={(e) => setNewMessage(e.target.value)}
            placeholder="Type your message..."
            disabled={loading}
          />
          <button type="submit" disabled={loading || !newMessage.trim()}>
            <Send size={20} />
          </button>
        </form>
      </div>
    </div>
  );
}