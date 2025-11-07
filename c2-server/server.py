"""
C2 Server - FastAPI Server for Command and Control System
Manages agent connections via SSE and provides REST API for dashboard
"""

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import StreamingResponse
from pydantic import BaseModel
from typing import Dict, Optional
from datetime import datetime
from queue import Queue
import asyncio
import json
import uuid

app = FastAPI(title="C2 Server", version="1.0.0")

# Configure CORS for Angular dashboard (port 4200)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:4200", "http://127.0.0.1:4200"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# ============================================================================
# Data Structures
# ============================================================================

# Connected agents: {agent_id: agent_info}
agents: Dict[str, dict] = {}

# Command queues per agent: {agent_id: Queue}
command_queues: Dict[str, Queue] = {}

# Task results: {task_id: result_info}
results: Dict[str, dict] = {}

# ============================================================================
# Pydantic Models
# ============================================================================

class AgentInfo(BaseModel):
    agent_id: str
    hostname: str
    connected_at: str
    last_seen: str
    status: str = "online"

class CommandRequest(BaseModel):
    agent_id: str
    command: str

class CommandResponse(BaseModel):
    task_id: str
    agent_id: str
    command: str
    status: str = "queued"

class MediaFile(BaseModel):
    file_path: str
    file_name: str
    type: str
    size_bytes: int
    size_mb: float
    created_at: str
    duration_minutes: Optional[float] = None

class StorageStats(BaseModel):
    total_files: int
    video_files: int
    total_size_mb: float
    video_size_mb: float
    base_path: str

class SessionInfo(BaseModel):
    session_key: str
    segment_count: int
    total_size_bytes: int
    total_size_mb: float
    start_time: str
    end_time: str
    duration_minutes: float
    date_folder: str
    segments: Optional[list[MediaFile]] = None

class RecordingStatus(BaseModel):
    is_recording: bool
    session_key: Optional[str] = None
    started_at: Optional[str] = None
    duration_seconds: int
    segment_count: int
    current_file: Optional[str] = None
    mode: str

class DatabaseStats(BaseModel):
    pending: int
    uploading: int
    done: int
    error: int
    total_size_mb: float

class UploadStatus(BaseModel):
    enabled: bool
    active_uploads: int
    endpoint: Optional[str] = None

class SystemInfo(BaseModel):
    os_version: str
    storage_path: str
    disk_space_gb: float

class AgentStatus(BaseModel):
    recording: RecordingStatus
    database: DatabaseStats
    upload: UploadStatus
    system: SystemInfo

class ResultRequest(BaseModel):
    task_id: str
    agent_id: str
    output: str
    error: Optional[str] = None
    exit_code: int = 0
    timestamp: str
    media_file: Optional[MediaFile] = None
    media_files: Optional[list[MediaFile]] = None
    storage_stats: Optional[StorageStats] = None
    sessions: Optional[list[SessionInfo]] = None
    agent_status: Optional[AgentStatus] = None

class ResultResponse(BaseModel):
    task_id: str
    agent_id: str
    command: str
    output: str
    error: Optional[str] = None
    exit_code: int
    timestamp: str
    status: str
    media_file: Optional[MediaFile] = None
    media_files: Optional[list[MediaFile]] = None
    storage_stats: Optional[StorageStats] = None
    sessions: Optional[list[SessionInfo]] = None
    agent_status: Optional[AgentStatus] = None

# ============================================================================
# Background Tasks
# ============================================================================

async def cleanup_disconnected_agents():
    """
    Background task to clean up agents that have been offline for more than 5 minutes
    """
    while True:
        try:
            await asyncio.sleep(60)  # Run every minute
            current_time = datetime.utcnow()
            agents_to_remove = []

            for agent_id, agent_info in list(agents.items()):
                last_seen = datetime.fromisoformat(agent_info["last_seen"])
                time_diff = (current_time - last_seen).total_seconds()

                # Remove agents offline for more than 5 minutes (300 seconds)
                if time_diff > 300:
                    agents_to_remove.append(agent_id)

            for agent_id in agents_to_remove:
                agents.pop(agent_id, None)
                command_queues.pop(agent_id, None)
                print(f"[SERVER] Cleaned up disconnected agent: {agent_id} (offline for {time_diff:.0f}s)")

        except Exception as e:
            print(f"[SERVER] Error in cleanup task: {e}")

@app.on_event("startup")
async def startup_event():
    """Start background tasks on server startup"""
    asyncio.create_task(cleanup_disconnected_agents())
    print("[SERVER] Started automatic cleanup of disconnected agents (5min timeout)")

# ============================================================================
# Endpoints
# ============================================================================

@app.get("/")
async def root():
    """Root endpoint - server status"""
    return {
        "status": "online",
        "service": "C2 Server",
        "version": "1.0.0",
        "agents_online": len(agents),
        "timestamp": datetime.utcnow().isoformat()
    }

@app.get("/agents")
async def list_agents():
    """List all connected agents"""
    agents_list = []
    current_time = datetime.utcnow()

    # Update agent status based on last_seen
    for agent_id, agent_info in list(agents.items()):
        last_seen = datetime.fromisoformat(agent_info["last_seen"])
        time_diff = (current_time - last_seen).total_seconds()

        # Consider agent offline if no heartbeat for more than 60 seconds
        if time_diff > 60:
            agent_info["status"] = "offline"

        agents_list.append(agent_info)

    return {"agents": agents_list, "count": len(agents_list)}

@app.get("/agent/stream/{agent_id}")
async def agent_stream(agent_id: str, hostname: str = "unknown"):
    """
    SSE Stream endpoint for agents
    Agent connects here and receives commands in real-time
    """

    # Remove any existing agents with the same hostname (deduplication)
    agents_to_remove = [aid for aid, info in agents.items()
                        if info.get("hostname") == hostname and aid != agent_id]
    for old_agent_id in agents_to_remove:
        agents.pop(old_agent_id, None)
        command_queues.pop(old_agent_id, None)
        print(f"[SERVER] Removed duplicate agent: {old_agent_id} (same hostname: {hostname})")

    # Register agent - preserve connected_at if agent already exists (reconnection)
    existing_connected_at = agents[agent_id]["connected_at"] if agent_id in agents else datetime.utcnow().isoformat()

    agents[agent_id] = {
        "agent_id": agent_id,
        "hostname": hostname,
        "connected_at": existing_connected_at,  # Preserve original connection time on reconnect
        "last_seen": datetime.utcnow().isoformat(),
        "status": "online"
    }

    # Create command queue for this agent if it doesn't exist
    if agent_id not in command_queues:
        command_queues[agent_id] = Queue()

    async def event_generator():
        """Generate SSE events for the agent"""
        try:
            while True:
                # Update last_seen timestamp
                if agent_id in agents:
                    agents[agent_id]["last_seen"] = datetime.utcnow().isoformat()

                # Check if there's a command in the queue
                if not command_queues[agent_id].empty():
                    command_data = command_queues[agent_id].get()

                    # Send command as SSE event
                    yield f"event: command\n"
                    yield f"data: {json.dumps(command_data)}\n\n"
                else:
                    # Send heartbeat to keep connection alive
                    yield f": heartbeat\n\n"

                # Wait before next check
                await asyncio.sleep(1)

        except asyncio.CancelledError:
            # Client disconnected
            if agent_id in agents:
                agents[agent_id]["status"] = "disconnected"
                agents[agent_id]["last_seen"] = datetime.utcnow().isoformat()

    return StreamingResponse(
        event_generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no"  # Disable nginx buffering
        }
    )

@app.post("/command", response_model=CommandResponse)
async def send_command(command_request: CommandRequest):
    """
    Send command to a specific agent
    Command is queued and will be sent via SSE stream
    """

    agent_id = command_request.agent_id
    command = command_request.command

    # Validate agent exists and is online
    if agent_id not in agents:
        raise HTTPException(status_code=404, detail="Agent not found")

    if agents[agent_id]["status"] != "online":
        raise HTTPException(status_code=400, detail="Agent is not online")

    # Generate unique task ID
    task_id = str(uuid.uuid4())

    # Create command object
    command_data = {
        "task_id": task_id,
        "command": command
    }

    # Add to agent's command queue
    command_queues[agent_id].put(command_data)

    # Store in results with pending status
    results[task_id] = {
        "task_id": task_id,
        "agent_id": agent_id,
        "command": command,
        "output": "",
        "error": None,
        "exit_code": -1,
        "timestamp": datetime.utcnow().isoformat(),
        "status": "queued"
    }

    return CommandResponse(
        task_id=task_id,
        agent_id=agent_id,
        command=command,
        status="queued"
    )

@app.post("/result")
async def receive_result(result_request: ResultRequest):
    """
    Receive command execution result from agent
    Agent sends result here after executing command
    """

    task_id = result_request.task_id

    # Check if task exists
    if task_id not in results:
        raise HTTPException(status_code=404, detail="Task not found")

    # Update result with all fields
    update_data = {
        "output": result_request.output,
        "error": result_request.error,
        "exit_code": result_request.exit_code,
        "timestamp": result_request.timestamp,
        "status": "completed"
    }

    # Add media fields if present
    if result_request.media_file:
        update_data["media_file"] = result_request.media_file.dict()

    if result_request.media_files:
        update_data["media_files"] = [f.dict() for f in result_request.media_files]

    if result_request.storage_stats:
        update_data["storage_stats"] = result_request.storage_stats.dict()

    if result_request.sessions:
        update_data["sessions"] = [s.dict() for s in result_request.sessions]

    if result_request.agent_status:
        update_data["agent_status"] = result_request.agent_status.dict()

    results[task_id].update(update_data)

    return {"status": "success", "message": "Result received"}

@app.get("/result/{task_id}", response_model=ResultResponse)
async def get_result(task_id: str):
    """
    Get result of a specific task
    Dashboard polls this endpoint to get command results
    """

    if task_id not in results:
        raise HTTPException(status_code=404, detail="Result not found")

    result = results[task_id]

    return ResultResponse(**result)

@app.get("/results")
async def list_results(limit: int = 50):
    """
    List recent command results
    Returns last N results sorted by timestamp
    """

    # Sort results by timestamp (newest first)
    sorted_results = sorted(
        results.values(),
        key=lambda x: x["timestamp"],
        reverse=True
    )

    # Limit results
    limited_results = sorted_results[:limit]

    return {"results": limited_results, "count": len(limited_results)}

@app.get("/media/{agent_id}")
async def get_agent_media(agent_id: str):
    """
    Get media files and storage statistics for a specific agent
    Returns list of video files captured by the agent
    """

    # Check if agent exists
    if agent_id not in agents:
        raise HTTPException(status_code=404, detail="Agent not found")

    # Find all results with media files for this agent
    agent_media_files = []
    agent_storage_stats = None

    for result in results.values():
        if result.get("agent_id") == agent_id:
            # Collect media files
            if result.get("media_file"):
                agent_media_files.append(result["media_file"])

            if result.get("media_files"):
                agent_media_files.extend(result["media_files"])

            # Get latest storage stats
            if result.get("storage_stats"):
                agent_storage_stats = result["storage_stats"]

    # Remove duplicates based on file_path
    unique_files = {}
    for file in agent_media_files:
        unique_files[file["file_path"]] = file

    media_list = list(unique_files.values())

    # Sort by created_at (newest first)
    media_list.sort(key=lambda x: x.get("created_at", ""), reverse=True)

    return {
        "agent_id": agent_id,
        "media_files": media_list,
        "count": len(media_list),
        "storage_stats": agent_storage_stats
    }

@app.get("/media/preview/{agent_id}/{filename:path}")
async def get_media_preview_url(agent_id: str, filename: str):
    """
    Get preview URL for a media file
    Returns the direct URL to the agent's HTTP server
    """

    # Check if agent exists
    if agent_id not in agents:
        raise HTTPException(status_code=404, detail="Agent not found")

    # For simplicity, agent's HTTP server runs on localhost:9000
    # In production, this would need to resolve the agent's actual IP/hostname
    # and potentially use a proxy or tunnel

    # Get agent info (could include HTTP port in future)
    agent_info = agents[agent_id]
    http_port = 9000  # Default port

    # Build preview URL
    # Note: This assumes agent is running on localhost
    # For remote agents, you would need to store and use the agent's IP/hostname
    preview_url = f"http://localhost:{http_port}/media/{filename}"

    return {
        "agent_id": agent_id,
        "filename": filename,
        "url": preview_url,
        "note": "Agent must be running on localhost or URL must be adjusted for remote access"
    }

@app.get("/media/{agent_id}/sessions")
async def get_agent_sessions(agent_id: str):
    """
    Get recording sessions for a specific agent
    Returns list of sessions with grouped segments
    """
    if agent_id not in agents:
        raise HTTPException(status_code=404, detail="Agent not found")
    
    # Send command to agent to list sessions
    task_id = str(uuid.uuid4())
    command_data = {
        "task_id": task_id,
        "command": "media:list-sessions",
        "type": "media:list-sessions"
    }
    
    if agent_id not in command_queues:
        command_queues[agent_id] = Queue()
    
    command_queues[agent_id].put(command_data)
    
    # Wait for result (with timeout)
    max_wait = 10  # seconds
    waited = 0
    while waited < max_wait:
        if task_id in results:
            result = results[task_id]
            return {
                "agent_id": agent_id,
                "sessions": result.get("sessions", []),
                "count": len(result.get("sessions", []))
            }
        await asyncio.sleep(0.5)
        waited += 0.5
    
    raise HTTPException(status_code=504, detail="Agent did not respond in time")

@app.get("/media/{agent_id}/session/{session_key}")
async def get_session_details(agent_id: str, session_key: str):
    """
    Get details of a specific recording session
    Returns session info with all segments
    """
    if agent_id not in agents:
        raise HTTPException(status_code=404, detail="Agent not found")
    
    # Send command to agent
    task_id = str(uuid.uuid4())
    command_data = {
        "task_id": task_id,
        "command": f"media:session-details",
        "type": "media:session-details",
        "session_key": session_key
    }
    
    if agent_id not in command_queues:
        command_queues[agent_id] = Queue()
    
    command_queues[agent_id].put(command_data)
    
    # Wait for result
    max_wait = 10
    waited = 0
    while waited < max_wait:
        if task_id in results:
            result = results[task_id]
            sessions = result.get("sessions", [])
            if sessions:
                return sessions[0]  # Return the single session
            raise HTTPException(status_code=404, detail="Session not found")
        await asyncio.sleep(0.5)
        waited += 0.5
    
    raise HTTPException(status_code=504, detail="Agent did not respond in time")

@app.delete("/agent/{agent_id}")
async def remove_agent(agent_id: str):
    """
    Remove agent from active list (admin endpoint)
    """

    if agent_id not in agents:
        raise HTTPException(status_code=404, detail="Agent not found")

    # Remove agent
    del agents[agent_id]

    # Clean up command queue
    if agent_id in command_queues:
        del command_queues[agent_id]

    return {"status": "success", "message": f"Agent {agent_id} removed"}

# ============================================================================
# Main
# ============================================================================

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000, log_level="info")
