using System.Collections.Generic;
using UnityEngine;

public class DropManager : MonoBehaviour
{
    private List<PlacedTile> _dropLocations = new List<PlacedTile>();
    private PlacedTile _tempPlacedTile;
    private GhostTile _currentLocation;
    private GhostTile _lastValidLocation;

    public bool isCurrentlyDragging = false;

    public List<PlacedTile> GetTilesDroppedThisTurn()
    {
        return _dropLocations;
    }

    public void SetTempGrabbedTile(PlacedTile tile)
    {
        _tempPlacedTile = tile;
    }

    public bool RemovedPlacedTile(PlacedTile placedTile)
    {
        return _dropLocations.Remove(placedTile);
    }

    public void ResetLocations()
    {
        _dropLocations.Clear();
        _tempPlacedTile = null;
        _currentLocation = null;
        _lastValidLocation = null;
    }

    // Where the pointer has been, forgotten. GetCurrentLocation falls back to
    // the last square the pointer crossed, so a tile let go in the gap
    // between squares still lands somewhere sensible - but that memory
    // outlived the drag that made it. The next drag, released between
    // squares, went to a square passed over during the one before, often one
    // with a tile already on it, and snapped back to the rack. The second
    // try worked because it was aimed more carefully. Every drag now starts
    // from nothing.
    public void ForgetWhereThePointerWas()
    {
        _currentLocation = null;
        _lastValidLocation = null;
    }

    public void SetCurrentLocation(GhostTile location)
    {
        _currentLocation = location;
        if (location != null)
            _lastValidLocation = location;
    }

    public GhostTile GetCurrentLocation()
    {
        return _currentLocation != null ? _currentLocation : _lastValidLocation;
    }

    public void ClearCurrentLocation(GhostTile location)
    {
        if (_currentLocation == location)
        {
            _currentLocation = null;
        }
    }

    public void AddLocation()
    {
        if (_tempPlacedTile != null)
        {
            if (_currentLocation != null)
            {
                _tempPlacedTile.letterPosition = _currentLocation.letterPosition;
            }

            if (!_dropLocations.Contains(_tempPlacedTile))
            {
                _dropLocations.Add(_tempPlacedTile);
            }
        }

        _tempPlacedTile = null;
    }
}