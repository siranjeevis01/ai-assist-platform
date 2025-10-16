// src/pages/Calendar.jsx
import React, { useState, useEffect } from 'react';
import { Plus, Calendar as CalendarIcon, MapPin, Clock, MoreVertical } from 'lucide-react';
import { eventService } from '../services/api';

export default function Calendar() {
  const [events, setEvents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [showNewEvent, setShowNewEvent] = useState(false);
  const [selectedEvent, setSelectedEvent] = useState(null);
  const [newEvent, setNewEvent] = useState({
    title: '',
    description: '',
    startUtc: '',
    endUtc: '',
    location: '',
    attendees: ''
  });

  useEffect(() => {
    loadEvents();
  }, []);

  const loadEvents = async () => {
    try {
      const today = new Date();
      const nextWeek = new Date(today.getTime() + 7 * 24 * 60 * 60 * 1000);
      
      const eventsData = await eventService.getAll(
        today.toISOString(),
        nextWeek.toISOString()
      );
      setEvents(eventsData);
    } catch (error) {
      console.error('Failed to load events:', error);
    } finally {
      setLoading(false);
    }
  };

  const createEvent = async (e) => {
    e.preventDefault();
    try {
      await eventService.create(newEvent);
      setShowNewEvent(false);
      setNewEvent({ 
        title: '', 
        description: '', 
        startUtc: '', 
        endUtc: '', 
        location: '',
        attendees: '' 
      });
      loadEvents();
    } catch (error) {
      console.error('Failed to create event:', error);
      alert('Failed to create event. Please try again.');
    }
  };

  const deleteEvent = async (eventId) => {
    if (window.confirm('Are you sure you want to delete this event?')) {
      try {
        await eventService.delete(eventId);
        loadEvents();
      } catch (error) {
        console.error('Failed to delete event:', error);
        alert('Failed to delete event.');
      }
    }
  };

  const formatEventTime = (startUtc, endUtc) => {
    const start = new Date(startUtc);
    const end = new Date(endUtc);
    
    const isSameDay = start.toDateString() === end.toDateString();
    
    if (isSameDay) {
      return {
        date: start.toLocaleDateString(),
        time: `${start.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} - ${end.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`
      };
    } else {
      return {
        date: `${start.toLocaleDateString()} - ${end.toLocaleDateString()}`,
        time: 'Multiple days'
      };
    }
  };

  const getUpcomingEvents = () => {
    const now = new Date();
    return events.filter(event => new Date(event.startUtc) >= now)
                .sort((a, b) => new Date(a.startUtc) - new Date(b.startUtc));
  };

  const getPastEvents = () => {
    const now = new Date();
    return events.filter(event => new Date(event.startUtc) < now)
                .sort((a, b) => new Date(b.startUtc) - new Date(a.startUtc));
  };

  const upcomingEvents = getUpcomingEvents();
  const pastEvents = getPastEvents();

  return (
    <div className="calendar-page">
      <div className="page-header">
        <div className="header-content">
          <h1>Calendar</h1>
          <p>Manage your events and schedule</p>
        </div>
        <button 
          className="btn btn-primary"
          onClick={() => setShowNewEvent(true)}
        >
          <Plus size={20} />
          New Event
        </button>
      </div>

      {/* New Event Modal */}
      {showNewEvent && (
        <div className="modal-overlay">
          <div className="modal">
            <h2>Create New Event</h2>
            <form onSubmit={createEvent}>
              <div className="form-group">
                <label>Title *</label>
                <input
                  type="text"
                  value={newEvent.title}
                  onChange={(e) => setNewEvent({ ...newEvent, title: e.target.value })}
                  required
                  placeholder="Meeting with team"
                />
              </div>
              <div className="form-group">
                <label>Description</label>
                <textarea
                  value={newEvent.description}
                  onChange={(e) => setNewEvent({ ...newEvent, description: e.target.value })}
                  rows="3"
                  placeholder="Brief description of the event..."
                />
              </div>
              <div className="form-row">
                <div className="form-group">
                  <label>Start Time *</label>
                  <input
                    type="datetime-local"
                    value={newEvent.startUtc}
                    onChange={(e) => setNewEvent({ ...newEvent, startUtc: e.target.value })}
                    required
                  />
                </div>
                <div className="form-group">
                  <label>End Time *</label>
                  <input
                    type="datetime-local"
                    value={newEvent.endUtc}
                    onChange={(e) => setNewEvent({ ...newEvent, endUtc: e.target.value })}
                    required
                  />
                </div>
              </div>
              <div className="form-group">
                <label>Location</label>
                <input
                  type="text"
                  value={newEvent.location}
                  onChange={(e) => setNewEvent({ ...newEvent, location: e.target.value })}
                  placeholder="Conference Room A"
                />
              </div>
              <div className="form-group">
                <label>Attendees</label>
                <input
                  type="text"
                  value={newEvent.attendees}
                  onChange={(e) => setNewEvent({ ...newEvent, attendees: e.target.value })}
                  placeholder="email1@example.com, email2@example.com"
                />
                <small>Separate multiple emails with commas</small>
              </div>
              <div className="modal-actions">
                <button 
                  type="button" 
                  className="btn btn-secondary"
                  onClick={() => setShowNewEvent(false)}
                >
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary">
                  Create Event
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Events Lists */}
      <div className="calendar-content">
        {/* Upcoming Events */}
        <div className="events-section">
          <h2>Upcoming Events ({upcomingEvents.length})</h2>
          <div className="events-list">
            {loading ? (
              <div className="loading">Loading events...</div>
            ) : upcomingEvents.length === 0 ? (
              <div className="empty-state">
                <CalendarIcon size={48} />
                <p>No upcoming events</p>
                <button 
                  className="btn btn-primary"
                  onClick={() => setShowNewEvent(true)}
                >
                  Schedule Your First Event
                </button>
              </div>
            ) : (
              upcomingEvents.map(event => {
                const { date, time } = formatEventTime(event.startUtc, event.endUtc);
                return (
                  <div key={event.id} className="event-card">
                    <div className="event-time">
                      <div className="event-date">{date}</div>
                      <div className="event-time-display">
                        <Clock size={14} />
                        {time}
                      </div>
                    </div>
                    <div className="event-content">
                      <h3>{event.title}</h3>
                      {event.description && (
                        <p className="event-description">{event.description}</p>
                      )}
                      <div className="event-details">
                        {event.location && (
                          <div className="event-location">
                            <MapPin size={14} />
                            {event.location}
                          </div>
                        )}
                        {event.attendees && event.attendees.length > 0 && (
                          <div className="event-attendees">
                            Attendees: {event.attendees}
                          </div>
                        )}
                      </div>
                    </div>
                    <div className="event-actions">
                      <div className={`event-status ${event.status?.toLowerCase()}`}>
                        {event.status || 'Scheduled'}
                      </div>
                      <button 
                        className="icon-btn"
                        onClick={() => deleteEvent(event.id)}
                      >
                        <MoreVertical size={16} />
                      </button>
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </div>

        {/* Past Events */}
        {pastEvents.length > 0 && (
          <div className="events-section">
            <h2>Past Events ({pastEvents.length})</h2>
            <div className="events-list">
              {pastEvents.map(event => {
                const { date, time } = formatEventTime(event.startUtc, event.endUtc);
                return (
                  <div key={event.id} className="event-card past">
                    <div className="event-time">
                      <div className="event-date">{date}</div>
                      <div className="event-time-display">{time}</div>
                    </div>
                    <div className="event-content">
                      <h3>{event.title}</h3>
                      {event.description && (
                        <p className="event-description">{event.description}</p>
                      )}
                      {event.location && (
                        <div className="event-location">
                          <MapPin size={14} />
                          {event.location}
                        </div>
                      )}
                    </div>
                    <div className="event-actions">
                      <div className="event-status completed">
                        Completed
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}