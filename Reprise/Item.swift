//
//  Item.swift
//  Reprise
//
//  Created by aodjo on 7/27/26.
//

import Foundation
import SwiftData

@Model
final class Item {
    var timestamp: Date
    
    init(timestamp: Date) {
        self.timestamp = timestamp
    }
}
