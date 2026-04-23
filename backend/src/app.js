const express = require('express');
const pool = require('./db');

const app = express();

app.use(express.json());

// DB 행을 API 응답 형태로 맞춘다.
function toRoom(row) {
  return {
    id: Number(row.id),
    name: row.name,
    connectionType: row.connection_type,
    connectionValue: row.connection_value,
    mapId: row.map_id,
    isPublic: Boolean(row.is_public),
    maxPlayers: row.max_players,
    currentPlayers: row.current_players,
    status: row.status,
    createdAt: row.created_at,
    updatedAt: row.updated_at
  };
}

async function fetchRoomById(connection, id, lock = false) {
  const [rows] = await connection.query(
    `SELECT * FROM rooms WHERE id = ?${lock ? ' FOR UPDATE' : ''}`,
    [id]
  );
  return rows[0] || null;
}

app.get('/health', async (req, res) => {
  res.json({
    ok: true,
    timestamp: new Date().toISOString()
  });
});

app.get('/rooms', async (req, res, next) => {
  try {
    const [rows] = await pool.query(
      'SELECT * FROM rooms WHERE is_public = TRUE AND status IN (?, ?) ORDER BY created_at DESC, id DESC',
      ['open', 'in_game']
    );
    res.json(rows.map(toRoom));
  } catch (error) {
    next(error);
  }
});

app.post('/rooms', async (req, res, next) => {
  try {
    const name = typeof req.body.name === 'string' ? req.body.name.trim() : '';
    const connectionType = typeof req.body.connectionType === 'string'
      ? req.body.connectionType.trim()
      : 'local';
    const connectionValue = typeof req.body.connectionValue === 'string'
      ? req.body.connectionValue.trim()
      : '';
    const mapId = typeof req.body.mapId === 'string' ? req.body.mapId.trim() : null;
    const isPublic = req.body.isPublic !== false;
    const maxPlayers = Number(req.body.maxPlayers ?? 4);
    const currentPlayers = Number(req.body.currentPlayers ?? 1);

    if (!name) {
      return res.status(400).json({ message: 'name is required' });
    }

    if (!connectionType || !connectionValue) {
      return res.status(400).json({ message: 'connectionType and connectionValue are required' });
    }

    if (!Number.isInteger(maxPlayers) || maxPlayers < 1) {
      return res.status(400).json({ message: 'maxPlayers must be a positive integer' });
    }

    if (!Number.isInteger(currentPlayers) || currentPlayers < 0 || currentPlayers > maxPlayers) {
      return res.status(400).json({ message: 'currentPlayers must be between 0 and maxPlayers' });
    }

    const [result] = await pool.query(
      `INSERT INTO rooms
        (name, connection_type, connection_value, map_id, is_public, max_players, current_players, status)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
      [name, connectionType, connectionValue, mapId || null, isPublic, maxPlayers, currentPlayers, 'open']
    );

    const [rows] = await pool.query('SELECT * FROM rooms WHERE id = ?', [result.insertId]);
    res.status(201).json(toRoom(rows[0]));
  } catch (error) {
    next(error);
  }
});

app.post('/rooms/:id/join', async (req, res, next) => {
  const roomId = Number(req.params.id);

  if (!Number.isInteger(roomId)) {
    return res.status(400).json({ message: 'invalid room id' });
  }

  const connection = await pool.getConnection();

  try {
    // 입장 중에는 해당 방을 잠금 상태로 확인한다.
    await connection.beginTransaction();

    const room = await fetchRoomById(connection, roomId, true);

    if (!room) {
      await connection.rollback();
      return res.status(404).json({ message: 'room not found' });
    }

    if (room.status !== 'open') {
      await connection.rollback();
      return res.status(409).json({ message: 'room is not open' });
    }

    if (room.current_players >= room.max_players) {
      await connection.rollback();
      return res.status(409).json({ message: 'room is full' });
    }

    await connection.query(
      'UPDATE rooms SET current_players = current_players + 1 WHERE id = ?',
      [roomId]
    );

    const updated = await fetchRoomById(connection, roomId);
    await connection.commit();
    res.json(toRoom(updated));
  } catch (error) {
    await connection.rollback();
    next(error);
  } finally {
    connection.release();
  }
});

app.post('/rooms/:id/leave', async (req, res, next) => {
  const roomId = Number(req.params.id);

  if (!Number.isInteger(roomId)) {
    return res.status(400).json({ message: 'invalid room id' });
  }

  const connection = await pool.getConnection();

  try {
    // 나가기 처리도 같은 방식으로 잠금을 건다.
    await connection.beginTransaction();

    const room = await fetchRoomById(connection, roomId, true);

    if (!room) {
      await connection.rollback();
      return res.status(404).json({ message: 'room not found' });
    }

    if (room.current_players <= 0) {
      await connection.rollback();
      return res.status(409).json({ message: 'room is already empty' });
    }

    await connection.query(
      'UPDATE rooms SET current_players = current_players - 1 WHERE id = ?',
      [roomId]
    );

    const updated = await fetchRoomById(connection, roomId);
    await connection.commit();
    res.json(toRoom(updated));
  } catch (error) {
    await connection.rollback();
    next(error);
  } finally {
    connection.release();
  }
});

app.patch('/rooms/:id', async (req, res, next) => {
  try {
    const roomId = Number(req.params.id);

    if (!Number.isInteger(roomId)) {
      return res.status(400).json({ message: 'invalid room id' });
    }

    const updates = [];
    const values = [];

    if (Object.prototype.hasOwnProperty.call(req.body, 'name')) {
      const name = typeof req.body.name === 'string' ? req.body.name.trim() : '';

      if (!name) {
        return res.status(400).json({ message: 'name cannot be empty' });
      }

      updates.push('name = ?');
      values.push(name);
    }

    if (Object.prototype.hasOwnProperty.call(req.body, 'connectionType')) {
      const connectionType = typeof req.body.connectionType === 'string' ? req.body.connectionType.trim() : '';

      if (!connectionType) {
        return res.status(400).json({ message: 'connectionType cannot be empty' });
      }

      updates.push('connection_type = ?');
      values.push(connectionType);
    }

    if (Object.prototype.hasOwnProperty.call(req.body, 'connectionValue')) {
      const connectionValue = typeof req.body.connectionValue === 'string' ? req.body.connectionValue.trim() : '';

      if (!connectionValue) {
        return res.status(400).json({ message: 'connectionValue cannot be empty' });
      }

      updates.push('connection_value = ?');
      values.push(connectionValue);
    }

    if (Object.prototype.hasOwnProperty.call(req.body, 'mapId')) {
      const mapId = typeof req.body.mapId === 'string' ? req.body.mapId.trim() : '';
      updates.push('map_id = ?');
      values.push(mapId || null);
    }

    if (Object.prototype.hasOwnProperty.call(req.body, 'isPublic')) {
      updates.push('is_public = ?');
      values.push(req.body.isPublic !== false);
    }

    if (Object.prototype.hasOwnProperty.call(req.body, 'maxPlayers')) {
      const maxPlayers = Number(req.body.maxPlayers);

      if (!Number.isInteger(maxPlayers) || maxPlayers < 1) {
        return res.status(400).json({ message: 'maxPlayers must be a positive integer' });
      }

      const [currentRows] = await pool.query('SELECT current_players FROM rooms WHERE id = ?', [roomId]);

      if (currentRows.length === 0) {
        return res.status(404).json({ message: 'room not found' });
      }

      if (maxPlayers < currentRows[0].current_players) {
        return res.status(400).json({ message: 'maxPlayers cannot be smaller than currentPlayers' });
      }

      updates.push('max_players = ?');
      values.push(maxPlayers);
    }

    if (Object.prototype.hasOwnProperty.call(req.body, 'status')) {
      const allowedStatuses = new Set(['open', 'closed', 'in_game']);
      const status = typeof req.body.status === 'string' ? req.body.status : '';

      if (!allowedStatuses.has(status)) {
        return res.status(400).json({ message: 'invalid status' });
      }

      updates.push('status = ?');
      values.push(status);
    }

    if (updates.length === 0) {
      return res.status(400).json({ message: 'no valid fields provided' });
    }

    values.push(roomId);

    const [result] = await pool.query(
      `UPDATE rooms SET ${updates.join(', ')} WHERE id = ?`,
      values
    );

    if (result.affectedRows === 0) {
      return res.status(404).json({ message: 'room not found' });
    }

    const [rows] = await pool.query('SELECT * FROM rooms WHERE id = ?', [roomId]);
    res.json(toRoom(rows[0]));
  } catch (error) {
    next(error);
  }
});

app.use((error, req, res, next) => {
  console.error(error);
  res.status(500).json({ message: 'internal server error' });
});

module.exports = app;
